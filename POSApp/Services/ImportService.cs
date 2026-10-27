using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class ImportService(IServiceScopeFactory scopes, IImportQueue queue, IConfiguration configuration)
{
    public const int MaximumFileBytes = 10 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ImportStatus> EnqueueAsync(ClaimsPrincipal actor, string kind, ImportInput input,
        CancellationToken ct = default)
    {
        ValidateKind(kind);
        if (input.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.Json)
            || Encoding.UTF8.GetByteCount(input.Json) > MaximumFileBytes
            || string.IsNullOrWhiteSpace(input.FileName) || input.FileName.Length > 255
            || input.Notes is null || input.Notes.Length > 2000)
        {
            throw new ValidationException("Choose a JSON file up to 10 MB and keep notes within 2,000 characters.");
        }
        var fingerprint = StoreOperation.Fingerprint(new { kind, input.Json, input.LocationId, input.Date, input.Notes });
        var status = await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var requiredRole = kind == "products" ? AppRoles.InventoryManager : AppRoles.Admin;
            var userId = await StoreAccess.RequireAsync(db, actor, requiredRole, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.ImportJobs.FindAsync([input.RequestId], ct);
            if (previous is not null)
            {
                StoreOperation.CheckReplay(userId, previous.UserId, previous.Fingerprint, fingerprint);
                return Status(previous);
            }
            var active = await db.ImportJobs.SingleOrDefaultAsync(job => job.ActiveKey == kind, ct);
            if (active is not null)
            {
                throw new ValidationException("An import of this type is already in progress. Wait for it to finish before uploading another file.");
            }
            var datedInput = kind == "suppliers"
                ? input with { Date = await AccountingCalendar.SelectedAsync(db, userId, configuration, ct) }
                : input;
            var job = new ImportJob
            {
                Id = input.RequestId,
                Kind = kind,
                ActiveKey = kind,
                Fingerprint = fingerprint,
                FileName = Path.GetFileName(input.FileName),
                Payload = JsonSerializer.Serialize(datedInput, JsonOptions),
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ImportJobs.Add(job);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Status(job);
        }, ct);
        queue.Notify();
        return status;
    }

    public async Task<IReadOnlyList<ImportStatus>> ReadAsync(ClaimsPrincipal actor, string kind,
        CancellationToken ct = default)
    {
        if (kind != "active")
        {
            ValidateKind(kind);
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor,
            kind == "active" ? AppRoles.SalesAssociate : kind == "products" ? AppRoles.InventoryManager : AppRoles.Admin, ct);
        return await db.ImportJobs.AsNoTracking().Where(job => kind == "active" ? job.ActiveKey != null : job.Kind == kind)
            .OrderByDescending(job => job.CreatedAt).Take(10)
            .Select(job => new ImportStatus(job.Id, job.Kind, job.FileName, job.Status,
                job.Message, job.CreatedAt, job.FinishedAt)).ToListAsync(ct);
    }

    internal static ImportStatus Status(ImportJob job) => new(job.Id, job.Kind, job.FileName,
        job.Status, job.Message, job.CreatedAt, job.FinishedAt);

    private static void ValidateKind(string kind)
    {
        if (kind is not ("suppliers" or "stock-adjustment" or "products"))
        {
            throw new ValidationException("Unknown import type.");
        }
    }
}

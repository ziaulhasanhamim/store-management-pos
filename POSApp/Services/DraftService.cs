using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class DraftService(IServiceScopeFactory scopes)
{
    private static string Role(string kind) => kind.Split(':')[0] switch
    {
        "stock-take" => AppRoles.InventoryManager,
        _ => throw new ValidationException("Server drafts are only supported for stock takes.")
    };

    private static void ValidateKind(string kind)
    {
        if (kind.Length is < 1 or > 100 || kind.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not '-' and not ':'))
        {
            throw new ValidationException("Invalid draft name.");
        }
        if (!kind.StartsWith("stock-take", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Server drafts are only supported for stock takes.");
        }
    }

    public async Task<EditorDraftDocument?> ReadAsync(ClaimsPrincipal actor, string kind, CancellationToken ct = default)
    {
        ValidateKind(kind);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, Role(kind), ct);
        return await db.EditorDrafts.AsNoTracking().Where(draft => draft.UserId == actorId && draft.Kind == kind)
            .Select(draft => new EditorDraftDocument(draft.Revision, draft.Payload, draft.UpdatedAt, draft.SaveId))
            .SingleOrDefaultAsync(ct);
    }

    public Task<EditorDraftDocument> SaveAsync(ClaimsPrincipal actor, string kind, EditorDraftInput input, CancellationToken ct = default)
    {
        ValidateKind(kind);
        if (input.SaveId == Guid.Empty || input.Payload is null || Encoding.UTF8.GetByteCount(input.Payload) > 2097152)
        {
            throw new ValidationException("This draft is too large to save.");
        }
        try
        {
            using var json = JsonDocument.Parse(input.Payload, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            throw new ValidationException("Invalid draft data.");
        }
        return StoreOperation.RunAsync<EditorDraftDocument>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, Role(kind), ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var draft = await db.EditorDrafts.SingleOrDefaultAsync(draft => draft.UserId == actorId && draft.Kind == kind, ct);
            if (draft?.SaveId == input.SaveId && draft.Payload == input.Payload)
            {
                return new(draft.Revision, draft.Payload, draft.UpdatedAt, draft.SaveId);
            }
            if ((draft?.Revision ?? 0) != input.Revision)
            {
                throw new ValidationException("This draft changed in another tab. Your local copy is kept. Reload to review the newer server draft.");
            }
            if (draft is null)
            {
                draft = new EditorDraft { UserId = actorId, Kind = kind, Payload = input.Payload };
                db.EditorDrafts.Add(draft);
            }
            draft.Payload = input.Payload;
            draft.Revision++;
            draft.SaveId = input.SaveId;
            draft.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(draft.Revision, draft.Payload, draft.UpdatedAt, draft.SaveId);
        }, ct);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class ImportWorker(IServiceScopeFactory scopes, IImportQueue queue, ILogger<ImportWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await RunNextAsync(stoppingToken))
                {
                    await queue.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Import worker could not access the queue.");
                await queue.WaitAsync(stoppingToken);
            }
        }
    }

    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var id = await db.ImportJobs.Where(job => job.Status == "Queued"
            || (job.Status == "Processing" && job.LeaseUntil < now))
            .OrderBy(job => job.CreatedAt).Select(job => (Guid?)job.Id).FirstOrDefaultAsync(ct);
        if (id is null)
        {
            return false;
        }
        var token = Guid.NewGuid();
        var claimed = await db.ImportJobs.Where(job => job.Id == id && (job.Status == "Queued"
            || (job.Status == "Processing" && job.LeaseUntil < now)))
            .ExecuteUpdateAsync(update => update
                .SetProperty(job => job.Status, "Processing")
                .SetProperty(job => job.LeaseToken, token)
                .SetProperty(job => job.LeaseUntil, now.AddMinutes(2)), ct);
        if (claimed == 0)
        {
            return true;
        }
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = RenewAsync(id.Value, token, lease);
        string? result = null;
        try
        {
            var job = await db.ImportJobs.AsNoTracking().SingleAsync(job => job.Id == id, ct);
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
                .FindByIdAsync(job.UserId.ToString())
                ?? throw new ValidationException("The user who uploaded this file no longer exists.");
            var actor = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<User>>()
                .CreateAsync(user);
            result = await scope.ServiceProvider.GetRequiredService<ImportProcessor>().ProcessAsync(id.Value, actor, lease.Token);
            await FinishAsync(id.Value, token, "Completed", result, ct);
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
            // Leave the durable job available for lease recovery after shutdown or lost ownership.
        }
        catch (Exception exception) when (result is null
            && exception is ValidationException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Import {ImportId} failed validation.", id);
            await FinishAsync(id.Value, token, "Failed", exception.Message, ct);
        }
        finally
        {
            await lease.CancelAsync();
            await heartbeat;
        }
        return true;
    }

    private async Task RenewAsync(Guid id, Guid token, CancellationTokenSource lease)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(lease.Token))
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var changed = await db.ImportJobs.Where(job => job.Id == id && job.LeaseToken == token
                    && job.Status == "Processing").ExecuteUpdateAsync(update => update
                        .SetProperty(job => job.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(2)), lease.Token);
                if (changed == 0)
                {
                    await lease.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Lost the lease for import {ImportId}.", id);
            await lease.CancelAsync();
        }
    }

    private async Task FinishAsync(Guid id, Guid token, string status, string message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ImportJobs.Where(job => job.Id == id && job.LeaseToken == token)
            .ExecuteUpdateAsync(update => update
                .SetProperty(job => job.Status, status)
                .SetProperty(job => job.Result, status == "Completed" ? message : null)
                .SetProperty(job => job.Message, message.Substring(0, Math.Min(message.Length, 2000)))
                .SetProperty(job => job.ActiveKey, (string?)null)
                .SetProperty(job => job.Payload, "")
                .SetProperty(job => job.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(job => job.FinishedAt, DateTimeOffset.UtcNow), ct);
    }
}

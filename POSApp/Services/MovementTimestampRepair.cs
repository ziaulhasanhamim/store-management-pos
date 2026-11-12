using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using POSApp.Persistence;

namespace POSApp.Services;

public static class MovementTimestampRepair
{
    private static readonly DateTimeOffset ValidThreshold = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task RepairAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(MovementTimestampRepair));
        var zone = AccountingCalendar.Zone(configuration);

        // 1. Repair CashMovements with ancient OccurredAt (e.g. year 0001)
        var brokenCash = await db.CashMovements
            .Include(m => m.Payment).ThenInclude(p => p!.Sale)
            .Include(m => m.SupplierPayment)
            .Include(m => m.Expense)
            .Include(m => m.DebtEntry)
            .Where(m => m.OccurredAt < ValidThreshold)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        if (brokenCash.Count > 0)
        {
            logger?.LogInformation("Repairing {Count} CashMovements with ancient OccurredAt...", brokenCash.Count);
            foreach (var m in brokenCash)
            {
                DateTimeOffset target;
                if (m.Payment?.Sale != null && m.Payment.Sale.SoldAt >= ValidThreshold)
                {
                    target = m.Payment.Sale.SoldAt;
                }
                else if (m.Payment != null && m.Payment.PaidAt >= ValidThreshold)
                {
                    target = m.Payment.PaidAt;
                }
                else if (m.SupplierPayment != null && m.SupplierPayment.PaidAt >= ValidThreshold)
                {
                    target = m.SupplierPayment.PaidAt;
                }
                else if (m.Expense != null && m.Expense.OccurredAt >= ValidThreshold)
                {
                    target = m.Expense.OccurredAt;
                }
                else if (m.DebtEntry != null && m.DebtEntry.OccurredAt >= ValidThreshold)
                {
                    target = m.DebtEntry.OccurredAt;
                }
                else
                {
                    target = m.CreatedAt >= ValidThreshold ? m.CreatedAt : DateTimeOffset.UtcNow;
                }

                // Preserve monotonic ordering using ID milliseconds
                m.OccurredAt = target.AddMilliseconds(m.Id % 1000);
            }
            await db.SaveChangesAsync(ct);
        }

        // 2. Repair BankMovements with ancient OccurredAt
        var brokenBank = await db.BankMovements
            .Include(m => m.Payment).ThenInclude(p => p!.Sale)
            .Include(m => m.SupplierPayment)
            .Include(m => m.Expense)
            .Include(m => m.DebtEntry)
            .Where(m => m.OccurredAt < ValidThreshold)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        if (brokenBank.Count > 0)
        {
            logger?.LogInformation("Repairing {Count} BankMovements with ancient OccurredAt...", brokenBank.Count);
            foreach (var m in brokenBank)
            {
                DateTimeOffset target;
                if (m.Payment?.Sale != null && m.Payment.Sale.SoldAt >= ValidThreshold)
                {
                    target = m.Payment.Sale.SoldAt;
                }
                else if (m.Payment != null && m.Payment.PaidAt >= ValidThreshold)
                {
                    target = m.Payment.PaidAt;
                }
                else if (m.SupplierPayment != null && m.SupplierPayment.PaidAt >= ValidThreshold)
                {
                    target = m.SupplierPayment.PaidAt;
                }
                else if (m.Expense != null && m.Expense.OccurredAt >= ValidThreshold)
                {
                    target = m.Expense.OccurredAt;
                }
                else if (m.DebtEntry != null && m.DebtEntry.OccurredAt >= ValidThreshold)
                {
                    target = m.DebtEntry.OccurredAt;
                }
                else
                {
                    target = m.CreatedAt >= ValidThreshold ? m.CreatedAt : DateTimeOffset.UtcNow;
                }

                m.OccurredAt = target.AddMilliseconds(m.Id % 1000);
            }
            await db.SaveChangesAsync(ct);
        }

        // 3. Repair DebtEntries with ancient OccurredAt
        var brokenDebt = await db.DebtEntries
            .Where(d => d.OccurredAt < ValidThreshold)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        if (brokenDebt.Count > 0)
        {
            logger?.LogInformation("Repairing {Count} DebtEntries with ancient OccurredAt...", brokenDebt.Count);
            foreach (var d in brokenDebt)
            {
                var target = d.CreatedAt >= ValidThreshold ? d.CreatedAt : DateTimeOffset.UtcNow;
                d.OccurredAt = target.AddMilliseconds(d.Id % 1000);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}

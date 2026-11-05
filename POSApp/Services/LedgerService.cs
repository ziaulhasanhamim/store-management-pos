using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Security;

namespace POSApp.Services;

public record PartyLedgerReport(
    int PartyId,
    string PartyName,
    string? Phone,
    string? Address,
    bool IsSupplier,
    DateOnly Start,
    DateOnly End,
    string Period,
    decimal OpeningBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ClosingBalance,
    IReadOnlyList<PartyLedgerRow> Entries
);

public record PartyLedgerRow(
    DateTimeOffset Date,
    string Subject,
    string? Reference,
    decimal? Debit,
    decimal? Credit,
    decimal Balance,
    string? Remarks
);

public sealed class LedgerService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public static (DateOnly Start, DateOnly End, string ResolvedPeriod) ResolveRange(
        string? period, DateOnly? start, DateOnly? end, TimeZoneInfo zone, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        var p = (period ?? "all").ToLowerInvariant();
        return p switch
        {
            "month" => (firstOfMonth, today, "month"),
            "lastmonth" => (firstOfMonth.AddMonths(-1), firstOfMonth.AddDays(-1), "lastmonth"),
            "30days" => (today.AddDays(-29), today, "30days"),
            "custom" when start.HasValue && end.HasValue =>
                (start.Value <= end.Value ? start.Value : end.Value,
                 start.Value <= end.Value ? end.Value : start.Value, "custom"),
            "custom" when start.HasValue => (start.Value, today, "custom"),
            "custom" when end.HasValue => (firstOfMonth, end.Value, "custom"),
            "all" => (new DateOnly(2020, 1, 1), today, "all"),
            _ => (new DateOnly(2020, 1, 1), today, "all")
        };
    }

    public async Task<PartyLedgerReport> ReadCustomerLedgerAsync(
        ClaimsPrincipal actor, int customerId, string? period, DateOnly? start, DateOnly? end, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Name, c.Phone, c.Address })
            .SingleOrDefaultAsync(ct);

        if (customer is null)
        {
            throw new KeyNotFoundException($"Customer with ID {customerId} was not found.");
        }

        var zone = AccountingCalendar.Zone(configuration);
        var (effectiveStart, effectiveEnd, resolvedPeriod) = ResolveRange(period, start, end, zone, DateTimeOffset.UtcNow);

        var fromUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            effectiveStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        var throughUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            effectiveEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        // Calculate opening balance as of effectiveStart:
        var priorEntry = await db.CustomerBalanceEntries.AsNoTracking()
            .Where(e => e.CustomerBalance!.CustomerId == customerId && e.OccurredAt < fromUtc)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.BalanceAfter })
            .FirstOrDefaultAsync(ct);

        decimal openingBalance;
        if (priorEntry != null)
        {
            openingBalance = priorEntry.BalanceAfter;
        }
        else
        {
            var hasLaterOpeningEntry = await db.CustomerBalanceEntries.AsNoTracking()
                .AnyAsync(e => e.CustomerBalance!.CustomerId == customerId && e.Type == BalanceEntryType.OpeningBalance && e.OccurredAt >= fromUtc, ct);

            if (hasLaterOpeningEntry)
            {
                openingBalance = 0m;
            }
            else
            {
                var balanceOpening = await db.CustomerBalances.AsNoTracking()
                    .Where(b => b.CustomerId == customerId)
                    .Select(b => (decimal?)b.OpeningBalance)
                    .FirstOrDefaultAsync(ct) ?? 0m;
                openingBalance = balanceOpening;
            }
        }

        var periodEntries = await db.CustomerBalanceEntries.AsNoTracking()
            .Include(e => e.Sale)
            .Include(e => e.Payment)
            .Where(e => e.CustomerBalance!.CustomerId == customerId && e.OccurredAt >= fromUtc && e.OccurredAt < throughUtc)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        var rows = new List<PartyLedgerRow>();
        decimal runningBalance = openingBalance;
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (var entry in periodEntries)
        {
            decimal? debit = null;
            decimal? credit = null;

            if (entry.Amount > 0)
            {
                debit = entry.Amount;
                totalDebit += entry.Amount;
                runningBalance += entry.Amount;
            }
            else if (entry.Amount < 0)
            {
                credit = Math.Abs(entry.Amount);
                totalCredit += credit.Value;
                runningBalance -= credit.Value;
            }

            string subject;
            string? reference = null;

            switch (entry.Type)
            {
                case BalanceEntryType.Invoice:
                    var receiptNo = entry.Sale?.ReceiptNumber ?? (entry.SaleId.HasValue ? $"#{entry.SaleId}" : "N/A");
                    subject = $"Sale invoice #{receiptNo}";
                    reference = receiptNo;
                    break;

                case BalanceEntryType.Payment:
                    var method = entry.Payment?.Method.ToString() ?? "Cash";
                    var payRef = entry.Payment?.Reference;
                    subject = $"Payment received ({method})" + (!string.IsNullOrWhiteSpace(payRef) ? $" · Ref: {payRef}" : "");
                    reference = payRef;
                    break;

                case BalanceEntryType.Adjustment:
                    subject = "Balance adjustment";
                    break;

                case BalanceEntryType.OpeningBalance:
                    subject = "Opening balance";
                    break;

                default:
                    subject = entry.Notes ?? "Transaction";
                    break;
            }

            var remarks = !string.IsNullOrWhiteSpace(entry.Notes)
                ? entry.Notes
                : (!string.IsNullOrWhiteSpace(entry.Payment?.Notes) ? entry.Payment.Notes : null);

            rows.Add(new PartyLedgerRow(
                Date: entry.OccurredAt,
                Subject: subject,
                Reference: reference,
                Debit: debit,
                Credit: credit,
                Balance: runningBalance,
                Remarks: remarks
            ));
        }

        return new PartyLedgerReport(
            PartyId: customer.Id,
            PartyName: customer.Name,
            Phone: customer.Phone,
            Address: customer.Address,
            IsSupplier: false,
            Start: effectiveStart,
            End: effectiveEnd,
            Period: resolvedPeriod,
            OpeningBalance: openingBalance,
            TotalDebit: totalDebit,
            TotalCredit: totalCredit,
            ClosingBalance: runningBalance,
            Entries: rows
        );
    }

    public async Task<PartyLedgerReport> ReadSupplierLedgerAsync(
        ClaimsPrincipal actor, int supplierId, string? period, DateOnly? start, DateOnly? end, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);

        var supplier = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == supplierId && !s.IsDeleted)
            .Select(s => new { s.Id, s.Name, s.Phone, s.Address })
            .SingleOrDefaultAsync(ct);

        if (supplier is null)
        {
            throw new KeyNotFoundException($"Supplier with ID {supplierId} was not found.");
        }

        var zone = AccountingCalendar.Zone(configuration);
        var (effectiveStart, effectiveEnd, resolvedPeriod) = ResolveRange(period, start, end, zone, DateTimeOffset.UtcNow);

        var fromUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            effectiveStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        var throughUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            effectiveEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        // Calculate opening balance as of effectiveStart:
        var priorEntry = await db.SupplierBalanceEntries.AsNoTracking()
            .Where(e => e.SupplierBalance!.SupplierId == supplierId && e.OccurredAt < fromUtc)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.BalanceAfter })
            .FirstOrDefaultAsync(ct);

        decimal openingBalance;
        if (priorEntry != null)
        {
            openingBalance = priorEntry.BalanceAfter;
        }
        else
        {
            var hasLaterOpeningEntry = await db.SupplierBalanceEntries.AsNoTracking()
                .AnyAsync(e => e.SupplierBalance!.SupplierId == supplierId && e.Type == BalanceEntryType.OpeningBalance && e.OccurredAt >= fromUtc, ct);

            if (hasLaterOpeningEntry)
            {
                openingBalance = 0m;
            }
            else
            {
                var balanceOpening = await db.SupplierBalances.AsNoTracking()
                    .Where(b => b.SupplierId == supplierId)
                    .Select(b => (decimal?)b.OpeningBalance)
                    .FirstOrDefaultAsync(ct) ?? 0m;
                openingBalance = balanceOpening;
            }
        }

        var periodEntries = await db.SupplierBalanceEntries.AsNoTracking()
            .Include(e => e.Purchase)
            .Include(e => e.SupplierPayment)
            .Where(e => e.SupplierBalance!.SupplierId == supplierId && e.OccurredAt >= fromUtc && e.OccurredAt < throughUtc)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        var rows = new List<PartyLedgerRow>();
        decimal runningBalance = openingBalance;
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (var entry in periodEntries)
        {
            decimal? debit = null;
            decimal? credit = null;

            if (entry.Amount > 0)
            {
                debit = entry.Amount;
                totalDebit += entry.Amount;
                runningBalance += entry.Amount;
            }
            else if (entry.Amount < 0)
            {
                credit = Math.Abs(entry.Amount);
                totalCredit += credit.Value;
                runningBalance -= credit.Value;
            }

            string subject;
            string? reference = null;

            switch (entry.Type)
            {
                case BalanceEntryType.Invoice:
                    var purchaseNo = entry.Purchase?.PurchaseNumber ?? (entry.PurchaseId.HasValue ? $"#{entry.PurchaseId}" : "N/A");
                    var billNo = entry.Purchase?.SupplierInvoiceNumber;
                    subject = $"Stock purchase #{purchaseNo}" + (!string.IsNullOrWhiteSpace(billNo) ? $" (Bill: {billNo})" : "");
                    reference = billNo ?? purchaseNo;
                    break;

                case BalanceEntryType.Payment:
                    var method = entry.SupplierPayment?.Method.ToString() ?? "Cash";
                    var payRef = entry.SupplierPayment?.Reference;
                    subject = $"Supplier payment ({method})" + (!string.IsNullOrWhiteSpace(payRef) ? $" · Ref: {payRef}" : "");
                    reference = payRef;
                    break;

                case BalanceEntryType.Adjustment:
                    subject = "Balance adjustment";
                    break;

                case BalanceEntryType.OpeningBalance:
                    subject = "Opening balance";
                    break;

                default:
                    subject = entry.Notes ?? "Transaction";
                    break;
            }

            var remarks = !string.IsNullOrWhiteSpace(entry.Notes)
                ? entry.Notes
                : (!string.IsNullOrWhiteSpace(entry.SupplierPayment?.Notes)
                    ? entry.SupplierPayment.Notes
                    : (!string.IsNullOrWhiteSpace(entry.Purchase?.Notes) ? entry.Purchase.Notes : null));

            rows.Add(new PartyLedgerRow(
                Date: entry.OccurredAt,
                Subject: subject,
                Reference: reference,
                Debit: debit,
                Credit: credit,
                Balance: runningBalance,
                Remarks: remarks
            ));
        }

        return new PartyLedgerReport(
            PartyId: supplier.Id,
            PartyName: supplier.Name,
            Phone: supplier.Phone,
            Address: supplier.Address,
            IsSupplier: true,
            Start: effectiveStart,
            End: effectiveEnd,
            Period: resolvedPeriod,
            OpeningBalance: openingBalance,
            TotalDebit: totalDebit,
            TotalCredit: totalCredit,
            ClosingBalance: runningBalance,
            Entries: rows
        );
    }
}

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

    /// <summary>
    /// Formats auto-generated GUID codes (e.g. REC-88a4...a8e9, PAY-cc31...f150, SAL-...)
    /// into a compact identifier combining the integer ID and last 4 characters of the GUID (e.g. #5-A8E9).
    /// Preserves short human-entered references as-is.
    /// </summary>
    public static string FormatShortReference(string? rawCode, int? id)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return id.HasValue ? $"#{id.Value}" : "";
        }

        var trimmed = rawCode.Trim();
        var isAutoGuid = trimmed.Length > 18 && (
            trimmed.StartsWith("REC-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("SAL-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("PAY-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("CUSTOMER-PAY-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("SALE-PAY-", StringComparison.OrdinalIgnoreCase) ||
            HasLongHexSegment(trimmed));

        if (!isAutoGuid)
        {
            var clean = trimmed.StartsWith('#') ? trimmed : $"#{trimmed}";
            return clean.Length > 22 ? clean[..22] + "…" : clean;
        }

        var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var hexPart = parts.FirstOrDefault(p => p.Length >= 20 && p.All(Uri.IsHexDigit))
                      ?? parts.LastOrDefault()
                      ?? trimmed;
        var last4 = hexPart.Length >= 4 ? hexPart[^4..].ToUpperInvariant() : hexPart.ToUpperInvariant();

        return id.HasValue ? $"#{id.Value}-{last4}" : $"#{last4}";
    }

    public static string? FormatRemarks(string? rawRemarks, int maxLength = 350)
    {
        if (string.IsNullOrWhiteSpace(rawRemarks))
        {
            return null;
        }

        var cleaned = rawRemarks.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd() + "…";
    }

    private static bool HasLongHexSegment(string value)
    {
        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => p.Length >= 20 && p.All(Uri.IsHexDigit));
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

        var displayStart = resolvedPeriod == "all" && periodEntries.Count > 0
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(periodEntries[0].OccurredAt, zone).DateTime)
            : (resolvedPeriod == "all" ? effectiveEnd : effectiveStart);

        var rows = new List<PartyLedgerRow>();
        decimal runningBalance = openingBalance;
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (var entry in periodEntries)
        {
            // Skip auto-generated empty 0-amount opening balance placeholders
            if (entry.Type == BalanceEntryType.OpeningBalance && entry.Amount == 0m && string.IsNullOrWhiteSpace(entry.Notes))
            {
                continue;
            }

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
                    var saleCode = FormatShortReference(entry.Sale?.ReceiptNumber, entry.SaleId ?? entry.Id);
                    subject = $"Sale invoice {saleCode}";
                    reference = saleCode;
                    break;

                case BalanceEntryType.Payment:
                    var method = entry.Payment?.Method.ToString() ?? "Cash";
                    var payCode = FormatShortReference(entry.Payment?.Reference, entry.PaymentId ?? entry.Id);
                    subject = !string.IsNullOrEmpty(payCode)
                        ? $"Payment ({method}) · {payCode}"
                        : $"Payment ({method})";
                    reference = payCode;
                    break;

                case BalanceEntryType.Adjustment:
                    subject = $"Adjustment #{entry.Id}";
                    break;

                case BalanceEntryType.OpeningBalance:
                    subject = "Opening balance";
                    break;

                default:
                    subject = $"Transaction #{entry.Id}";
                    break;
            }

            var rawNote = !string.IsNullOrWhiteSpace(entry.Notes)
                ? entry.Notes
                : (!string.IsNullOrWhiteSpace(entry.Payment?.Notes) ? entry.Payment.Notes : null);

            rows.Add(new PartyLedgerRow(
                Date: entry.OccurredAt,
                Subject: subject,
                Reference: reference,
                Debit: debit,
                Credit: credit,
                Balance: runningBalance,
                Remarks: FormatRemarks(rawNote)
            ));
        }

        return new PartyLedgerReport(
            PartyId: customer.Id,
            PartyName: customer.Name,
            Phone: customer.Phone,
            Address: customer.Address,
            IsSupplier: false,
            Start: displayStart,
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

        var displayStart = resolvedPeriod == "all" && periodEntries.Count > 0
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(periodEntries[0].OccurredAt, zone).DateTime)
            : (resolvedPeriod == "all" ? effectiveEnd : effectiveStart);

        var rows = new List<PartyLedgerRow>();
        decimal runningBalance = openingBalance;
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (var entry in periodEntries)
        {
            // Skip auto-generated empty 0-amount opening balance placeholders
            if (entry.Type == BalanceEntryType.OpeningBalance && entry.Amount == 0m && string.IsNullOrWhiteSpace(entry.Notes))
            {
                continue;
            }

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
                    var purchaseCode = FormatShortReference(entry.Purchase?.PurchaseNumber, entry.PurchaseId ?? entry.Id);
                    var billNo = entry.Purchase?.SupplierInvoiceNumber?.Trim();
                    subject = $"Purchase {purchaseCode}" + (!string.IsNullOrWhiteSpace(billNo) ? $" (Bill: {billNo})" : "");
                    reference = billNo ?? purchaseCode;
                    break;

                case BalanceEntryType.Payment:
                    var method = entry.SupplierPayment?.Method.ToString() ?? "Cash";
                    var payCode = FormatShortReference(entry.SupplierPayment?.Reference, entry.SupplierPaymentId ?? entry.Id);
                    subject = !string.IsNullOrEmpty(payCode)
                        ? $"Payment ({method}) · {payCode}"
                        : $"Payment ({method})";
                    reference = payCode;
                    break;

                case BalanceEntryType.Adjustment:
                    subject = $"Adjustment #{entry.Id}";
                    break;

                case BalanceEntryType.OpeningBalance:
                    subject = "Opening balance";
                    break;

                default:
                    subject = $"Transaction #{entry.Id}";
                    break;
            }

            var rawNote = !string.IsNullOrWhiteSpace(entry.Notes)
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
                Remarks: FormatRemarks(rawNote)
            ));
        }

        return new PartyLedgerReport(
            PartyId: supplier.Id,
            PartyName: supplier.Name,
            Phone: supplier.Phone,
            Address: supplier.Address,
            IsSupplier: true,
            Start: displayStart,
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

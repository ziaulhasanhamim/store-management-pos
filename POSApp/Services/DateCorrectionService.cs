using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed record DateCorrectionCandidate(
    int Id,
    string RecordType,
    string DisplayTitle,
    DateTimeOffset CurrentTimestamp,
    DateOnly BusinessDate,
    decimal Amount,
    string? Reference,
    string? Details
);

public sealed class DateCorrectionService(IServiceScopeFactory scopes, IConfiguration? configuration = null)
{
    public async Task ChangeDateAsync(
        ClaimsPrincipal actor,
        string recordType,
        int recordId,
        DateTimeOffset newTimestamp,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var normalizedType = (recordType ?? "").Trim().ToLowerInvariant();

        await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            await ApplyCorrectionAsync(db, normalizedType, recordType!, recordId, newTimestamp, reason, actorId, ct);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task<int> BulkChangeDateAsync(
        ClaimsPrincipal actor,
        string recordType,
        IEnumerable<int> recordIds,
        DateTimeOffset newTimestamp,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var normalizedType = recordType.Trim().ToLowerInvariant();
        var ids = recordIds.Distinct().ToArray();
        if (ids.Length == 0) return 0;

        return await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            int count = 0;
            foreach (var id in ids)
            {
                await ApplyCorrectionAsync(db, normalizedType, recordType, id, newTimestamp, reason, actorId, ct);
                count++;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return count;
        }, ct);
    }

    public async Task<IReadOnlyList<DateCorrectionCandidate>> SearchCandidatesAsync(
        ClaimsPrincipal actor,
        string recordType,
        string? query,
        DateOnly? from,
        DateOnly? to,
        int limit = 50,
        CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);

            var normalizedType = (recordType ?? "sale").Trim().ToLowerInvariant();
            var q = (query ?? "").Trim();
            var zone = AccountingCalendar.Zone(configuration);
            DateTimeOffset? fromUtc = from.HasValue ? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero) : null;
            DateTimeOffset? toUtc = to.HasValue ? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero) : null;
            var results = new List<DateCorrectionCandidate>();

            switch (normalizedType)
            {
                case "sale":
                {
                    var baseQuery = db.Sales.AsNoTracking().Where(s => !s.IsDeleted);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(s => s.SoldAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(s => s.SoldAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(s => s.Id == id || s.ReceiptNumber.Contains(q));
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(s => s.ReceiptNumber.Contains(q)
                                || (s.Customer != null && (s.Customer.Name.Contains(q) || (s.Customer.Phone != null && s.Customer.Phone.Contains(q)))));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(s => s.SoldAt)
                        .ThenByDescending(s => s.Id)
                        .Take(limit)
                        .Select(s => new
                        {
                            s.Id,
                            s.ReceiptNumber,
                            CustomerName = s.Customer != null ? s.Customer.Name : null,
                            s.SoldAt,
                            s.TotalAmount,
                            PaidAmount = s.Payments.Where(p => !p.IsVoided).Sum(p => p.Amount),
                            ItemCount = s.Items.Count
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(s => new DateCorrectionCandidate(
                        s.Id,
                        "sale",
                        $"Receipt #{s.ReceiptNumber} · {(string.IsNullOrWhiteSpace(s.CustomerName) ? "Walk-in" : s.CustomerName)}",
                        s.SoldAt,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.SoldAt, zone).DateTime),
                        s.TotalAmount,
                        s.ReceiptNumber,
                        $"Items: {s.ItemCount} · Paid: BDT {s.PaidAmount:N2}"
                    )));
                    break;
                }
                case "expense":
                {
                    var baseQuery = db.Expenses.AsNoTracking().Where(e => !e.IsVoided);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(e => e.OccurredAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(e => e.OccurredAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(e => e.Id == id || (e.ReceiptReference != null && e.ReceiptReference.Contains(q)));
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(e => e.Notes.Contains(q)
                                || (e.CategoryDefinition != null && e.CategoryDefinition.Name.Contains(q))
                                || (e.ReceiptReference != null && e.ReceiptReference.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(e => e.OccurredAt)
                        .ThenByDescending(e => e.Id)
                        .Take(limit)
                        .Select(e => new
                        {
                            e.Id,
                            Category = e.CategoryDefinition != null ? e.CategoryDefinition.Name : e.Category.ToString(),
                            e.OccurredAt,
                            e.Amount,
                            e.Notes,
                            e.ReceiptReference,
                            e.StockLocationId
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(e => new DateCorrectionCandidate(
                        e.Id,
                        "expense",
                        $"Expense · {e.Category}",
                        e.OccurredAt,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.OccurredAt, zone).DateTime),
                        e.Amount,
                        e.ReceiptReference ?? $"Loc #{e.StockLocationId}",
                        e.Notes
                    )));
                    break;
                }
                case "supplierpayment":
                case "supplier-payment":
                {
                    var baseQuery = db.SupplierPayments.AsNoTracking().Where(p => !p.IsVoided);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(p => p.PaidAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(p => p.PaidAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(p => p.Id == id || (p.Reference != null && p.Reference.Contains(q)));
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(p => (p.SupplierBalance != null && p.SupplierBalance.Supplier != null && p.SupplierBalance.Supplier.Name.Contains(q))
                                || (p.Reference != null && p.Reference.Contains(q))
                                || (p.Notes != null && p.Notes.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(p => p.PaidAt)
                        .ThenByDescending(p => p.Id)
                        .Take(limit)
                        .Select(p => new
                        {
                            p.Id,
                            SupplierName = p.SupplierBalance != null && p.SupplierBalance.Supplier != null ? p.SupplierBalance.Supplier.Name : "Supplier",
                            p.PaidAt,
                            p.Amount,
                            p.Reference,
                            p.Method,
                            p.Notes
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(p => new DateCorrectionCandidate(
                        p.Id,
                        "supplier-payment",
                        $"Supplier Payment · {p.SupplierName}",
                        p.PaidAt,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.PaidAt, zone).DateTime),
                        p.Amount,
                        p.Reference ?? $"Payment #{p.Id}",
                        $"Method: {p.Method}{(string.IsNullOrWhiteSpace(p.Notes) ? "" : $" · {p.Notes}")}"
                    )));
                    break;
                }
                case "customerpayment":
                case "customer-payment":
                case "payment":
                {
                    var baseQuery = db.Payments.AsNoTracking().Where(p => !p.IsVoided);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(p => p.PaidAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(p => p.PaidAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(p => p.Id == id || (p.Reference != null && p.Reference.Contains(q)));
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(p => (p.CustomerBalance != null && p.CustomerBalance.Customer != null && p.CustomerBalance.Customer.Name.Contains(q))
                                || (p.Reference != null && p.Reference.Contains(q))
                                || (p.Notes != null && p.Notes.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(p => p.PaidAt)
                        .ThenByDescending(p => p.Id)
                        .Take(limit)
                        .Select(p => new
                        {
                            p.Id,
                            CustomerName = p.CustomerBalance != null && p.CustomerBalance.Customer != null ? p.CustomerBalance.Customer.Name : "Customer",
                            p.PaidAt,
                            p.Amount,
                            p.Reference,
                            p.Method,
                            p.Notes
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(p => new DateCorrectionCandidate(
                        p.Id,
                        "payment",
                        $"Customer Collection · {p.CustomerName}",
                        p.PaidAt,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.PaidAt, zone).DateTime),
                        p.Amount,
                        p.Reference ?? $"Receipt #{p.Id}",
                        $"Method: {p.Method}{(string.IsNullOrWhiteSpace(p.Notes) ? "" : $" · {p.Notes}")}"
                    )));
                    break;
                }
                case "purchase":
                case "receiving":
                {
                    var baseQuery = db.Purchases.AsNoTracking().Where(p => !p.IsDeleted);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(p => p.OrderedAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(p => p.OrderedAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(p => p.Id == id || (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.Contains(q)));
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(p => (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.Contains(q))
                                || (p.Supplier != null && p.Supplier.Name.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(p => p.OrderedAt)
                        .ThenByDescending(p => p.Id)
                        .Take(limit)
                        .Select(p => new
                        {
                            p.Id,
                            p.SupplierInvoiceNumber,
                            SupplierName = p.Supplier != null ? p.Supplier.Name : "Supplier",
                            At = p.ReceivedAt ?? p.OrderedAt,
                            p.TotalAmount,
                            PaidAmount = p.Payments.Where(x => !x.IsVoided).Sum(x => x.Amount),
                            ItemCount = p.Items.Count
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(p => new DateCorrectionCandidate(
                        p.Id,
                        "purchase",
                        $"Purchase #{(p.SupplierInvoiceNumber ?? p.Id.ToString())} · {p.SupplierName}",
                        p.At,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.At, zone).DateTime),
                        p.TotalAmount,
                        p.SupplierInvoiceNumber ?? $"PO #{p.Id}",
                        $"Items: {p.ItemCount} · Paid: BDT {p.PaidAmount:N2}"
                    )));
                    break;
                }
                case "debt":
                case "debtentry":
                case "debt-entry":
                {
                    var baseQuery = db.DebtEntries.AsNoTracking().Where(d => !d.IsVoided);
                    if (from.HasValue) baseQuery = baseQuery.Where(d => d.BusinessDate >= from.Value);
                    if (to.HasValue) baseQuery = baseQuery.Where(d => d.BusinessDate <= to.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(d => d.Id == id);
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(d => (d.LenderBalance != null && d.LenderBalance.Lender != null && d.LenderBalance.Lender.Name.Contains(q))
                                || (d.Notes != null && d.Notes.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(d => d.CreatedAt)
                        .ThenByDescending(d => d.Id)
                        .Take(limit)
                        .Select(d => new
                        {
                            d.Id,
                            LenderName = d.LenderBalance != null && d.LenderBalance.Lender != null ? d.LenderBalance.Lender.Name : "Lender",
                            d.Kind,
                            d.CreatedAt,
                            d.BusinessDate,
                            d.Amount,
                            d.Method,
                            d.Notes
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(d => new DateCorrectionCandidate(
                        d.Id,
                        "debt",
                        $"Debt ({d.Kind}) · {d.LenderName}",
                        d.CreatedAt,
                        d.BusinessDate,
                        d.Amount,
                        $"Debt #{d.Id}",
                        $"Kind: {d.Kind} · Method: {d.Method}{(string.IsNullOrWhiteSpace(d.Notes) ? "" : $" · {d.Notes}")}"
                    )));
                    break;
                }
                case "payroll":
                {
                    var baseQuery = db.PayrollEntries.AsNoTracking().Where(p => !p.IsVoided);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(p => p.OccurredAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(p => p.OccurredAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(p => p.Id == id);
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(p => (p.StaffAccount != null && p.StaffAccount.Employee != null && p.StaffAccount.Employee.Name.Contains(q))
                                || (p.Notes != null && p.Notes.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(p => p.OccurredAt)
                        .ThenByDescending(p => p.Id)
                        .Take(limit)
                        .Select(p => new
                        {
                            p.Id,
                            EmployeeName = p.StaffAccount != null && p.StaffAccount.Employee != null ? p.StaffAccount.Employee.Name : "Staff",
                            p.Kind,
                            p.OccurredAt,
                            p.PaidAmount,
                            p.Notes
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(p => new DateCorrectionCandidate(
                        p.Id,
                        "payroll",
                        $"Payroll ({p.Kind}) · {p.EmployeeName}",
                        p.OccurredAt,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.OccurredAt, zone).DateTime),
                        p.PaidAmount,
                        $"Payroll #{p.Id}",
                        $"Kind: {p.Kind}{(string.IsNullOrWhiteSpace(p.Notes) ? "" : $" · {p.Notes}")}"
                    )));
                    break;
                }
                case "stocktransfer":
                case "stock-transfer":
                case "transfer":
                {
                    var baseQuery = db.StockTransfers.AsNoTracking().Where(t => !t.IsVoided);
                    if (fromUtc.HasValue) baseQuery = baseQuery.Where(t => t.OccurredAt >= fromUtc.Value);
                    if (toUtc.HasValue) baseQuery = baseQuery.Where(t => t.OccurredAt < toUtc.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(t => t.Id == id);
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(t => (t.SourceLocation != null && t.SourceLocation.Name.Contains(q))
                                || (t.DestinationLocation != null && t.DestinationLocation.Name.Contains(q))
                                || (t.Notes != null && t.Notes.Contains(q)));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(t => t.OccurredAt)
                        .ThenByDescending(t => t.Id)
                        .Take(limit)
                        .Select(t => new
                        {
                            t.Id,
                            SourceName = t.SourceLocation != null ? t.SourceLocation.Name : "Source",
                            DestName = t.DestinationLocation != null ? t.DestinationLocation.Name : "Dest",
                            t.OccurredAt,
                            ItemCount = t.Items.Count,
                            t.Notes
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(t => new DateCorrectionCandidate(
                        t.Id,
                        "stock-transfer",
                        $"Stock Transfer · {t.SourceName} ➔ {t.DestName}",
                        t.OccurredAt,
                        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.OccurredAt, zone).DateTime),
                        0m,
                        $"Transfer #{t.Id}",
                        $"Items: {t.ItemCount}{(string.IsNullOrWhiteSpace(t.Notes) ? "" : $" · {t.Notes}")}"
                    )));
                    break;
                }
                case "cashtransfer":
                case "cash-transfer":
                {
                    var baseQuery = db.CashTransfers.AsNoTracking();
                    if (from.HasValue) baseQuery = baseQuery.Where(t => t.BusinessDate >= from.Value);
                    if (to.HasValue) baseQuery = baseQuery.Where(t => t.BusinessDate <= to.Value);
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        if (int.TryParse(q, out var id))
                        {
                            baseQuery = baseQuery.Where(t => t.Id == id);
                        }
                        else
                        {
                            baseQuery = baseQuery.Where(t => t.Notes != null && t.Notes.Contains(q));
                        }
                    }

                    var items = await baseQuery
                        .OrderByDescending(t => t.CreatedAt)
                        .ThenByDescending(t => t.Id)
                        .Take(limit)
                        .Select(t => new
                        {
                            t.Id,
                            t.SourceDrawerId,
                            t.DestinationDrawerId,
                            t.CreatedAt,
                            t.BusinessDate,
                            t.Amount,
                            t.Notes
                        })
                        .ToListAsync(ct);

                    results.AddRange(items.Select(t => new DateCorrectionCandidate(
                        t.Id,
                        "cash-transfer",
                        $"Cash Transfer #{t.Id} · Drawer #{t.SourceDrawerId} ➔ #{t.DestinationDrawerId}",
                        t.CreatedAt,
                        t.BusinessDate,
                        t.Amount,
                        $"Transfer #{t.Id}",
                        t.Notes ?? ""
                    )));
                    break;
                }
                default:
                    break;
            }

            return (IReadOnlyList<DateCorrectionCandidate>)results;
        }, ct);
    }

    private async Task ApplyCorrectionAsync(
        AppDbContext db,
        string normalizedType,
        string recordType,
        int recordId,
        DateTimeOffset newTimestamp,
        string reason,
        int actorId,
        CancellationToken ct)
    {
        newTimestamp = newTimestamp.ToUniversalTime();
        string beforeStr = "";
        string afterStr = newTimestamp.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var newDateOnly = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(newTimestamp, AccountingCalendar.Zone(configuration)).DateTime);

        switch (normalizedType)
        {
            case "sale":
            {
                var sale = await db.Sales
                    .Include(s => s.Items)
                    .Include(s => s.Payments)
                    .SingleOrDefaultAsync(s => s.Id == recordId, ct)
                    ?? throw new ValidationException($"Sale #{recordId} not found.");

                beforeStr = sale.SoldAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                sale.SoldAt = newTimestamp;

                var saleItemIds = sale.Items.Select(i => i.Id).ToList();
                if (saleItemIds.Count > 0)
                {
                    var invMovements = await db.InventoryMovements
                        .Where(m => m.SaleItemId != null && saleItemIds.Contains(m.SaleItemId.Value))
                        .ToListAsync(ct);
                    foreach (var m in invMovements) m.OccurredAt = newTimestamp;
                }

                var paymentIds = sale.Payments.Select(p => p.Id).ToList();
                foreach (var payment in sale.Payments)
                {
                    payment.PaidAt = newTimestamp;
                }

                var customerEntries = await db.CustomerBalanceEntries
                    .Where(e => e.SaleId == recordId || (e.PaymentId != null && paymentIds.Contains(e.PaymentId.Value)))
                    .ToListAsync(ct);
                foreach (var e in customerEntries) e.OccurredAt = newTimestamp;

                if (paymentIds.Count > 0)
                {
                    var cashMovements = await db.CashMovements
                        .Where(m => m.PaymentId != null && paymentIds.Contains(m.PaymentId.Value))
                        .ToListAsync(ct);
                    foreach (var m in cashMovements)
                    {
                        m.CreatedAt = newTimestamp;
                        m.BusinessDate = newDateOnly;
                    }

                    var bankMovements = await db.BankMovements
                        .Where(m => m.PaymentId != null && paymentIds.Contains(m.PaymentId.Value))
                        .ToListAsync(ct);
                    foreach (var m in bankMovements)
                    {
                        m.CreatedAt = newTimestamp;
                        m.BusinessDate = newDateOnly;
                    }
                }

                break;
            }
            case "expense":
            {
                var expense = await db.Expenses.SingleOrDefaultAsync(e => e.Id == recordId, ct)
                    ?? throw new ValidationException($"Expense #{recordId} not found.");

                beforeStr = expense.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                expense.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.ExpenseId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.ExpenseId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "supplierpayment":
            case "supplier-payment":
            {
                var payment = await db.SupplierPayments.SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Supplier payment #{recordId} not found.");

                beforeStr = payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                payment.PaidAt = newTimestamp;

                var balanceEntries = await db.SupplierBalanceEntries.Where(b => b.SupplierPaymentId == recordId).ToListAsync(ct);
                foreach (var b in balanceEntries) b.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.SupplierPaymentId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.SupplierPaymentId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "customerpayment":
            case "customer-payment":
            case "payment":
            {
                var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Payment #{recordId} not found.");

                beforeStr = payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                payment.PaidAt = newTimestamp;

                var balanceEntries = await db.CustomerBalanceEntries.Where(b => b.PaymentId == recordId).ToListAsync(ct);
                foreach (var b in balanceEntries) b.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.PaymentId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.PaymentId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "purchase":
            case "receiving":
            {
                var purchase = await db.Purchases
                    .Include(p => p.Items)
                    .SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Purchase #{recordId} not found.");

                beforeStr = (purchase.ReceivedAt ?? purchase.OrderedAt).ToString("yyyy-MM-dd HH:mm:ss zzz");
                purchase.OrderedAt = newTimestamp;
                if (purchase.ReceivedAt != null) purchase.ReceivedAt = newTimestamp;

                var purchaseItemIds = purchase.Items.Select(i => i.Id).ToList();
                if (purchaseItemIds.Count > 0)
                {
                    var invMovements = await db.InventoryMovements
                        .Where(m => m.PurchaseItemId != null && purchaseItemIds.Contains(m.PurchaseItemId.Value))
                        .ToListAsync(ct);
                    foreach (var m in invMovements) m.OccurredAt = newTimestamp;
                }

                var supplierEntries = await db.SupplierBalanceEntries
                    .Where(e => e.PurchaseId == recordId)
                    .ToListAsync(ct);
                foreach (var e in supplierEntries) e.OccurredAt = newTimestamp;

                var payments = await db.SupplierPayments.Where(p => p.PurchaseId == recordId).ToListAsync(ct);
                foreach (var p in payments)
                {
                    p.PaidAt = newTimestamp;
                    var cash = await db.CashMovements.Where(m => m.SupplierPaymentId == p.Id).ToListAsync(ct);
                    foreach (var c in cash)
                    {
                        c.CreatedAt = newTimestamp;
                        c.BusinessDate = newDateOnly;
                    }
                    var bank = await db.BankMovements.Where(m => m.SupplierPaymentId == p.Id).ToListAsync(ct);
                    foreach (var b in bank)
                    {
                        b.CreatedAt = newTimestamp;
                        b.BusinessDate = newDateOnly;
                    }
                }

                break;
            }
            case "debt":
            case "debtentry":
            case "debt-entry":
            {
                var debt = await db.DebtEntries
                    .SingleOrDefaultAsync(d => d.Id == recordId, ct)
                    ?? throw new ValidationException($"Debt entry #{recordId} not found.");

                beforeStr = debt.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                debt.BusinessDate = newDateOnly;
                debt.CreatedAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.DebtEntryId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.DebtEntryId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "payroll":
            {
                var payroll = await db.PayrollEntries.SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Payroll entry #{recordId} not found.");

                beforeStr = payroll.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                payroll.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.PayrollEntryId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.PayrollEntryId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "stocktransfer":
            case "stock-transfer":
            case "transfer":
            {
                var transfer = await db.StockTransfers
                    .SingleOrDefaultAsync(t => t.Id == recordId, ct)
                    ?? throw new ValidationException($"Stock transfer #{recordId} not found.");

                beforeStr = transfer.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                transfer.OccurredAt = newTimestamp;

                var invMovements = await db.InventoryMovements
                    .Where(m => m.StockTransferId == transfer.Id)
                    .ToListAsync(ct);
                foreach (var m in invMovements) m.OccurredAt = newTimestamp;

                break;
            }
            case "cashtransfer":
            case "cash-transfer":
            {
                var transfer = await db.CashTransfers.SingleOrDefaultAsync(t => t.Id == recordId, ct)
                    ?? throw new ValidationException($"Cash transfer #{recordId} not found.");

                beforeStr = transfer.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                transfer.BusinessDate = newDateOnly;
                transfer.CreatedAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.CashTransferId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            default:
                throw new ValidationException($"Unsupported record type '{recordType}'.");
        }

        db.RecordChanges.Add(new RecordChange
        {
            RequestId = Guid.NewGuid(),
            RecordType = recordType,
            RecordId = recordId,
            Action = "EditDate",
            Reason = reason.Trim(),
            Before = beforeStr,
            After = afterStr,
            CreatedByUserId = actorId
        });
    }
}

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed partial class ReportService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public static ReportRange ResolveRange(string? period, DateOnly? start, DateOnly? end, TimeZoneInfo zone, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var first = new DateOnly(today.Year, today.Month, 1);
        (var from, var through) = period switch
        {
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "week" => (today.AddDays(-6), today),
            "month" => (first, today),
            "lastmonth" => (first.AddMonths(-1), first.AddDays(-1)),
            "year" => (new DateOnly(today.Year, 1, 1), today),
            "custom" => (start ?? today, end ?? today),
            _ => (today, today)
        };
        if (through < from || through == DateOnly.MaxValue)
        {
            throw new ValidationException("Choose an end date on or after the start date.");
        }
        DateTimeOffset Utc(DateOnly date)
        {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        }
        return new(from, through, Utc(from), Utc(through.AddDays(1)));
    }

    public async Task<ReportPage> ReadAsync(ClaimsPrincipal actor, bool sales, string? period, DateOnly? start, DateOnly? end,
        int page = 1, CancellationToken ct = default, string? search = null, string? sort = null, int? locationId = null, int pageSize = 30)
    {
        return await StoreOperation.RunAsync<ReportPage>(async () =>
        {
            var limit = Math.Clamp(pageSize, 1, 10000);
            var range = ResolveRange(period, start, end, AccountingCalendar.Zone(configuration), DateTimeOffset.UtcNow);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, sales ? AppRoles.SalesAssociate : AppRoles.InventoryManager, ct);
            var canViewCosts = await CanViewCostsAsync(db, actorId, ct);
            var locations = await LocationsAsync(db, locationId, ct);
            var term = (search ?? "").Trim().ToLowerInvariant();
            var normalizedTerm = NameNormalization.Normalize(term);
            var numericId = int.TryParse(term.TrimStart('#'), out var parsed) ? parsed : -1;
            IQueryable<ReportQueryRow> rows;
            IQueryable<Sale>? saleQuery = null;
            if (sales)
            {
                saleQuery = db.Sales.AsNoTracking().Where(sale => !sale.IsDeleted && sale.Status == SaleStatus.Completed
                    && sale.SoldAt >= range.FromUtc && sale.SoldAt < range.ToUtc && (canViewCosts || sale.CashierId == actorId)
                    && (locationId == null || sale.StockLocationId == locationId));
                if (term.Length > 0)
                {
                    saleQuery = saleQuery.Where(sale => sale.Id == numericId || sale.ReceiptNumber.ToLower().Contains(term)
                        || (sale.Customer == null ? "walk-in" : sale.Customer.NormalizedName).Contains(normalizedTerm));
                }
                rows = saleQuery.Select(sale => new ReportQueryRow
                {
                    Id = sale.Id,
                    Number = sale.ReceiptNumber,
                    Party = sale.Customer == null ? "Walk-in" : sale.Customer.Name,
                    At = sale.SoldAt,
                    Total = sale.TotalAmount,
                    Paid = sale.Payments.Select(payment => (decimal?)payment.Amount).Sum() ?? 0,
                    Cost = canViewCosts ? sale.Items.Select(item => (decimal?)Math.Round(item.UnitCost * item.Quantity, 2)).Sum() ?? 0 : 0
                });
            }
            else
            {
                var purchases = db.Purchases.AsNoTracking().Where(purchase => !purchase.IsDeleted && purchase.Status == PurchaseStatus.Received
                    && purchase.ReceivedAt >= range.FromUtc && purchase.ReceivedAt < range.ToUtc
                    && (locationId == null || purchase.StockLocationId == locationId));
                if (term.Length > 0)
                {
                    purchases = purchases.Where(purchase => purchase.Id == numericId || purchase.PurchaseNumber.ToLower().Contains(term)
                        || purchase.Supplier!.NormalizedName.Contains(normalizedTerm)
                        || purchase.SupplierInvoiceNumber != null && purchase.SupplierInvoiceNumber.ToLower().Contains(term));
                }
                rows = purchases.Select(purchase => new ReportQueryRow
                {
                    Id = purchase.Id,
                    Number = purchase.PurchaseNumber,
                    Party = purchase.Supplier!.Name,
                    At = purchase.ReceivedAt!.Value,
                    Total = purchase.TotalAmount,
                    Paid = purchase.Payments.Select(payment => (decimal?)payment.Amount).Sum() ?? 0
                });
            }
            var order = string.IsNullOrWhiteSpace(sort) ? "id" : sort;
            if (!canViewCosts && order is "cost" or "cost-desc" or "profit" or "profit-desc")
            {
                throw new UnauthorizedAccessException("Cost and profit reports require inventory access.");
            }
            var summary = await rows.GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Total = g.Sum(row => (decimal?)row.Total) ?? 0m,
                    Paid = g.Sum(row => (decimal?)row.Paid) ?? 0m
                })
                .FirstOrDefaultAsync(ct);

            var count = summary?.Count ?? 0;
            var total = summary?.Total ?? 0m;
            var paid = summary?.Paid ?? 0m;
            var current = Math.Clamp(page, 1, Math.Max(1, (count + limit - 1) / limit));
            var ordered = order switch
            {
                "id" => rows.OrderBy(row => row.Id),
                "id-desc" => rows.OrderByDescending(row => row.Id),
                "date" => rows.OrderBy(row => row.At),
                "party" => rows.OrderBy(row => row.Party),
                "party-desc" => rows.OrderByDescending(row => row.Party),
                "total" => rows.OrderBy(row => row.Total),
                "total-desc" => rows.OrderByDescending(row => row.Total),
                "due" => rows.OrderBy(row => row.Total - row.Paid),
                "due-desc" => rows.OrderByDescending(row => row.Total - row.Paid),
                "paid" => rows.OrderBy(row => row.Paid),
                "paid-desc" => rows.OrderByDescending(row => row.Paid),
                "cost" => rows.OrderBy(row => row.Cost),
                "cost-desc" => rows.OrderByDescending(row => row.Cost),
                "profit" => rows.OrderBy(row => row.Total - row.Cost),
                "profit-desc" => rows.OrderByDescending(row => row.Total - row.Cost),
                _ => rows.OrderBy(row => row.Id)
            };
            var result = await ordered.ThenByDescending(row => row.Id).Skip((current - 1) * limit).Take(limit).ToArrayAsync(ct);
            var details = new Dictionary<int, SalesReportLine[]>();
            decimal? cost = null;
            if (saleQuery is not null)
            {
                if (canViewCosts)
                {
                    cost = await saleQuery.SelectMany(sale => sale.Items)
                        .Select(item => (decimal?)(item.UnitCost * item.Quantity))
                        .SumAsync(ct) ?? 0;
                }
                var ids = result.Select(row => row.Id).ToArray();
                if (ids.Length > 0)
                {
                    var detailQuery = saleQuery.Where(sale => ids.Contains(sale.Id));
                    var snapshots = await ReadLinesAsync(detailQuery, ct);
                    details = snapshots.Where(row => ids.Contains(row.SaleId)).GroupBy(row => row.SaleId)
                        .ToDictionary(group => group.Key, group => group.Select(row => row.Line(canViewCosts)).ToArray());
                }
            }
            return new(result.Select(row => new ReportRow(row.Id, row.Number, row.Party, row.At, row.Total, row.Paid)
            {
                Cost = sales && canViewCosts ? details.GetValueOrDefault(row.Id, []).Sum(item => item.Cost ?? 0) : null,
                Items = details.GetValueOrDefault(row.Id, [])
            }).ToArray(), count, total, paid, range, current)
            {
                CanViewCosts = canViewCosts,
                Cost = cost,
                Locations = locations
            };
        }, ct);
    }

    public async Task<PurchaseDetail?> PurchaseAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var purchase = await db.Purchases.AsNoTracking().Include(item => item.Items).Include(item => item.Payments)
            .Include(item => item.Supplier).Include(item => item.StockLocation)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.Id == id && item.Status == PurchaseStatus.Received, ct);
        return purchase is null ? null : new(new ReportRow(purchase.Id, purchase.PurchaseNumber, purchase.Supplier!.Name,
            purchase.ReceivedAt!.Value, purchase.TotalAmount, purchase.Payments.Sum(payment => payment.Amount)),
            purchase.StockLocation!.Name, purchase.SupplierInvoiceNumber, purchase.Notes,
            purchase.Items.Select(item => new PurchaseDetailLine(item.ProductName, item.ProductSku, item.Unit, item.Quantity, item.UnitCost, item.TotalAmount)).ToArray());
    }

    public async Task<StockLossReportPage> StockLossReportAsync(
        ClaimsPrincipal actor, string? period, DateOnly? start, DateOnly? end,
        int? locationId = null, string? search = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<StockLossReportPage>(async () =>
        {
            var range = ResolveRange(period, start, end, AccountingCalendar.Zone(configuration), DateTimeOffset.UtcNow);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            var locations = await LocationsAsync(db, locationId, ct);

            var term = (search ?? "").Trim().ToLowerInvariant();

            var movementsQuery = db.InventoryMovements.AsNoTracking()
                .Where(m => m.Quantity < 0
                    && (m.StockTakeItemId != null || m.StockAdjustmentItemId != null || m.Type == InventoryMovementType.Adjustment)
                    && m.OccurredAt >= range.FromUtc && m.OccurredAt < range.ToUtc
                    && (locationId == null || m.StockLocationId == locationId.Value));

            if (!string.IsNullOrWhiteSpace(term))
            {
                movementsQuery = movementsQuery.Where(m =>
                    m.Product!.Name.ToLower().Contains(term)
                    || (m.Product.Barcode != null && m.Product.Barcode.Contains(term))
                    || (m.Notes != null && m.Notes.ToLower().Contains(term)));
            }

            var totalQuantity = await movementsQuery.SumAsync(m => (decimal?)-m.Quantity, ct) ?? 0m;
            var totalFinancialLoss = await movementsQuery.SumAsync(m => (decimal?)(-m.Quantity * m.UnitCost), ct) ?? 0m;

            var rawRows = await movementsQuery
                .OrderByDescending(m => m.OccurredAt)
                .Select(m => new
                {
                    m.OccurredAt,
                    LocationName = m.StockLocation!.Name,
                    m.ProductId,
                    ProductName = m.Product!.Name,
                    m.Product.Barcode,
                    m.Product.Unit,
                    m.Quantity,
                    m.UnitCost,
                    m.Notes,
                    IsStockTake = m.StockTakeItemId != null
                })
                .ToListAsync(ct);

            var reasons = rawRows.GroupBy(r => DetermineReason(r.IsStockTake, r.Notes))
                .Select(g => new StockLossReasonTotal(
                    g.Key,
                    g.Sum(x => -x.Quantity),
                    g.Sum(x => decimal.Round(-x.Quantity * x.UnitCost, 2, MidpointRounding.AwayFromZero))))
                .OrderByDescending(g => g.Amount)
                .ToList();

            var limit = Math.Clamp(pageSize, 1, 500);
            var totalCount = rawRows.Count;
            var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)limit)));
            var pagedRows = rawRows.Skip((current - 1) * limit).Take(limit)
                .Select(r => new StockLossReportRow(
                    r.OccurredAt,
                    r.LocationName,
                    r.ProductId,
                    r.ProductName,
                    r.Barcode,
                    r.Unit,
                    DetermineReason(r.IsStockTake, r.Notes),
                    -r.Quantity,
                    r.UnitCost,
                    decimal.Round(-r.Quantity * r.UnitCost, 2, MidpointRounding.AwayFromZero),
                    r.Notes))
                .ToList();

            return new StockLossReportPage(
                pagedRows,
                range,
                totalQuantity,
                totalFinancialLoss,
                reasons,
                current)
            {
                Locations = locations,
                Pagination = new PageInfo(current, limit, totalCount)
            };
        }, ct);
    }

    private static string DetermineReason(bool isStockTake, string? notes)
    {
        if (isStockTake) return "Stock Take Shortage";
        if (string.IsNullOrWhiteSpace(notes)) return "Damage / Waste";
        if (notes.StartsWith("Damaged", StringComparison.OrdinalIgnoreCase)) return "Damaged / Broken";
        if (notes.StartsWith("Expired", StringComparison.OrdinalIgnoreCase)) return "Expired";
        if (notes.StartsWith("Promotional Gift", StringComparison.OrdinalIgnoreCase) || notes.StartsWith("Gift", StringComparison.OrdinalIgnoreCase)) return "Promotional Gift / Sample";
        if (notes.StartsWith("Internal Store Use", StringComparison.OrdinalIgnoreCase) || notes.StartsWith("Store Use", StringComparison.OrdinalIgnoreCase)) return "Internal Store Use";
        if (notes.StartsWith("Loss", StringComparison.OrdinalIgnoreCase) || notes.StartsWith("Shrinkage", StringComparison.OrdinalIgnoreCase)) return "Loss / Shrinkage";
        return "Manual Write-Off";
    }
    private static async Task<IReadOnlyList<Lookup>> LocationsAsync(AppDbContext db, int? locationId, CancellationToken ct)
    {
        var locations = await db.StockLocations.AsNoTracking().TagWith("Reports.Locations").OrderBy(location => location.Name)
            .Select(location => new Lookup(location.Id, location.Name + (location.IsDeleted ? " (archived)" : "")))
            .ToArrayAsync(ct);
        if (locationId is { } selected && !locations.Any(location => location.Id == selected))
        {
            throw new ValidationException("Choose a valid stock location or all locations.");
        }
        return locations;
    }

    private sealed class ReportQueryRow
    {
        public int Id { get; set; }
        public string Number { get; set; } = "";
        public string Party { get; set; } = "";
        public DateTimeOffset At { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Cost { get; set; }
    }

}

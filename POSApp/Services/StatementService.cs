using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class StatementService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<BalanceStatement> ReadAsync(ClaimsPrincipal actor, string account, string? period = "today",
        DateOnly? start = null, DateOnly? end = null, int? drawerId = null, int page = 1, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<BalanceStatement>(async () =>
        {
            if (account is not ("cash" or "bank"))
            {
                throw new ValidationException("Choose a cash drawer or the bank account.");
            }
            var today = AccountingCalendar.Today(configuration);
            if (period == "year")
            {
                start = new(today.Year, 1, 1);
                end = today;
                period = "custom";
            }
            var range = ReportService.ResolveRange(period, start, end, AccountingCalendar.Zone(configuration), DateTimeOffset.UtcNow);
            if (range.End.DayNumber - range.Start.DayNumber > 3660)
            {
                throw new ValidationException("Choose a statement range of up to ten years.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            IQueryable<Entry> events;
            IReadOnlyList<Lookup> drawers = [];
            decimal undatedOpening = 0;
            string name;
            if (account == "cash")
            {
                var options = await db.CashDrawers.AsNoTracking().OrderBy(drawer => drawer.StockLocation!.Name)
                    .Select(drawer => new { drawer.Id, drawer.StockLocationId, Name = drawer.StockLocation == null
                        ? "Legacy drawer #" + drawer.Id : drawer.StockLocation.Name }).ToArrayAsync(ct);
                drawers = options.Select(drawer => new Lookup(drawer.Id, drawer.Name)).ToArray();
                var location = await db.Users.Where(user => user.Id == actorId).Select(user => user.CashLocationId).SingleAsync(ct);
                drawerId ??= options.FirstOrDefault(drawer => drawer.StockLocationId == location)?.Id ?? options.FirstOrDefault()?.Id;
                if (drawerId is not null && !options.Any(drawer => drawer.Id == drawerId))
                {
                    throw new ValidationException("Choose a valid cash drawer.");
                }
                var drawer = await db.CashDrawers.AsNoTracking().SingleOrDefaultAsync(drawer => drawer.Id == drawerId, ct);
                name = options.FirstOrDefault(drawer => drawer.Id == drawerId)?.Name ?? "Cash drawer";
                var moves = db.CashMovements.AsNoTracking().Where(move => move.CashDrawerId == drawerId || (move.CashSession != null && move.CashSession.CashDrawerId == drawerId));
                var counts = db.CashSessions.AsNoTracking().Where(session => session.CashDrawerId == drawerId
                    && session.ClosedAt != null && session.Difference != null && session.Difference != 0
                    && !db.CashMovements.Any(m => (m.CashDrawerId == drawerId || (m.CashSession != null && m.CashSession.CashDrawerId == drawerId)) && m.Kind == CashMovementKind.Variance && m.CreatedAt == session.ClosedAt));
                var initial = (drawer != null && drawer.OpeningBalance != 0)
                    ? drawer.OpeningBalance
                    : ((drawer?.CurrentCash ?? 0)
                        - (await moves.Select(move => (decimal?)move.Amount).SumAsync(ct) ?? 0)
                        - (await counts.Select(session => session.Difference).SumAsync(ct) ?? 0));
                undatedOpening = drawer?.OpeningDate is null ? initial : 0;
                events = moves.Select(move => new Entry
                {
                    Id = move.Id,
                    Date = move.BusinessDate,
                    At = move.CreatedAt,
                    Priority = 1,
                    Kind = move.Kind,
                    Amount = move.Amount,
                    Notes = move.Notes,
                    Reference = "Cash movement #" + move.Id,
                    Reconciliation = move.Kind == CashMovementKind.Variance,
                    Collection = move.Payment != null && move.Payment.SaleId == null
                }).Concat(counts.Select(session => new Entry
                {
                    Id = session.Id,
                    Date = session.BusinessDate,
                    At = session.ClosedAt!.Value,
                    Priority = 1,
                    Kind = CashMovementKind.Reversal,
                    Amount = session.Difference!.Value,
                    Notes = session.ClosingNotes ?? "Cash counted at closing",
                    Reference = "Drawer count #" + session.Id,
                    Reconciliation = true,
                    Collection = false
                })).Concat(db.CashDrawers.Where(item => item.Id == drawerId && item.OpeningDate != null && initial != 0)
                    .Select(item => new Entry
                    {
                        Id = item.Id,
                        Date = item.OpeningDate!.Value,
                        At = item.CreatedAt,
                        Priority = 0,
                        Kind = CashMovementKind.OpeningBalance,
                        Amount = item.OpeningBalance != 0 ? item.OpeningBalance : initial,
                        Notes = "Initial drawer cash",
                        Reference = "Opening balance",
                        Reconciliation = false,
                        Collection = false
                    }));
            }
            else
            {
                var bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(bank => bank.Code == "MAIN", ct);
                name = "Bank & digital balance";
                var moves = db.BankMovements.AsNoTracking().Where(move => move.BankAccountId == (bank == null ? -1 : bank.Id));
                var initial = (bank != null && bank.OpeningBalance != 0)
                    ? bank.OpeningBalance
                    : ((bank?.CurrentBalance ?? 0) - (await moves.Select(move => (decimal?)move.Amount).SumAsync(ct) ?? 0));
                undatedOpening = bank?.OpeningDate is null ? initial : 0;
                events = moves.Select(move => new Entry
                {
                    Id = move.Id,
                    Date = move.BusinessDate,
                    At = move.CreatedAt,
                    Priority = 1,
                    Kind = move.Kind,
                    Amount = move.Amount,
                    Notes = move.Notes,
                    Reference = "Bank movement #" + move.Id,
                    Reconciliation = false,
                    Collection = move.Payment != null && move.Payment.SaleId == null
                }).Concat(db.BankAccounts.Where(item => item.Code == "MAIN" && item.OpeningDate != null && initial != 0)
                    .Select(item => new Entry
                    {
                        Id = item.Id,
                        Date = item.OpeningDate!.Value,
                        At = item.CreatedAt,
                        Priority = 0,
                        Kind = CashMovementKind.OpeningBalance,
                        Amount = item.OpeningBalance != 0 ? item.OpeningBalance : initial,
                        Notes = "Imported bank balance",
                        Reference = "Opening balance",
                        Reconciliation = false,
                        Collection = false
                    }));
                drawerId = null;
            }
            // Initial migration balances on the first day are opening funds, not that day's receipts.
            var opening = undatedOpening + (await events.Where(row => row.Date < range.Start
                    || (row.Date == range.Start && row.Kind == CashMovementKind.OpeningBalance))
                .Select(row => (decimal?)row.Amount).SumAsync(ct) ?? 0);
            var visible = events.Where(row => row.Date >= range.Start && row.Date <= range.End
                && !(row.Date == range.Start && row.Kind == CashMovementKind.OpeningBalance));
            var totals = await visible.GroupBy(row => 1).Select(group => new
            {
                Count = group.Count(),
                MoneyIn = group.Sum(row => row.Amount > 0 ? row.Amount : 0),
                MoneyOut = -group.Sum(row => row.Amount < 0 ? row.Amount : 0),
                Contributions = group.Sum(row => row.Kind == CashMovementKind.OwnerContribution ? row.Amount : 0),
                Withdrawals = -group.Sum(row => row.Kind == CashMovementKind.OwnerWithdrawal ? row.Amount : 0)
            }).SingleOrDefaultAsync(ct);
            var count = totals?.Count ?? 0;
            var currentPage = Math.Clamp(page, 1, Math.Max(1, (count + 49) / 50));
            var ordered = visible.OrderBy(row => row.Date).ThenBy(row => row.Priority).ThenBy(row => row.At)
                .ThenBy(row => row.Reconciliation).ThenBy(row => row.Id);
            var prefix = currentPage == 1 ? 0
                : await ordered.Take((currentPage - 1) * 50).Select(row => (decimal?)row.Amount).SumAsync(ct) ?? 0;
            var rows = await ordered.Skip((currentPage - 1) * 50).Take(50).ToArrayAsync(ct);
            var moneyIn = totals?.MoneyIn ?? 0;
            var moneyOut = totals?.MoneyOut ?? 0;
            var contributions = totals?.Contributions ?? 0;
            var withdrawals = totals?.Withdrawals ?? 0;
            var balance = opening + prefix;
            var result = rows.Select(row =>
            {
                balance += row.Amount;
                return new StatementRow(row.Date, row.At, Label(row), row.Reference, row.Notes, row.Amount, balance);
            }).ToArray();
            var pendingCount = (account == "cash" && drawerId != null)
                ? await db.CashCounts.AsNoTracking()
                    .Where(c => c.CashDrawerId == drawerId.Value && c.Status == CashCountStatus.PendingReview && !c.IsVoided)
                    .OrderByDescending(c => c.Id)
                    .Select(c => new { c.Difference, c.Notes, c.CountedAt })
                    .FirstOrDefaultAsync(ct)
                : null;

            return new(name, range, drawers, drawerId, opening, moneyIn, moneyOut, opening + moneyIn - moneyOut,
                contributions, withdrawals, result, count, currentPage)
            {
                PageOpening = opening + prefix,
                PendingCountDifference = pendingCount?.Difference,
                PendingCountNotes = pendingCount?.Notes,
                PendingCountAt = pendingCount?.CountedAt
            };
        }, ct);
    }

    public async Task<StockStatement> StockAsync(ClaimsPrincipal actor, int? productId = null, int? locationId = null,
        string? period = "month", DateOnly? start = null, DateOnly? end = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<StockStatement>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);

            var availableProducts = await db.Products.AsNoTracking().Where(p => !p.IsDeleted && !p.IsNonStocked)
                .OrderBy(p => p.Name)
                .Select(p => new Lookup(p.Id, p.Name + (p.Barcode != null ? $" ({p.Barcode})" : "")))
                .ToListAsync(ct);

            var availableLocations = await db.StockLocations.AsNoTracking().Where(l => !l.IsDeleted)
                .OrderBy(l => l.Name)
                .Select(l => new Lookup(l.Id, l.Name))
                .ToListAsync(ct);

            productId ??= availableProducts.FirstOrDefault()?.Id;
            if (productId is null)
            {
                throw new ValidationException("No stockable products found.");
            }

            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId.Value && !p.IsDeleted, ct)
                ?? throw new ValidationException("Product not found.");

            var range = ReportService.ResolveRange(period, start, end, AccountingCalendar.Zone(configuration), DateTimeOffset.UtcNow);

            var query = db.InventoryMovements.AsNoTracking()
                .Include(m => m.StockLocation)
                .Include(m => m.PurchaseItem).ThenInclude(pi => pi!.Purchase)
                .Include(m => m.SaleItem).ThenInclude(si => si!.Sale)
                .Include(m => m.StockAdjustmentItem).ThenInclude(ai => ai!.StockAdjustment)
                .Include(m => m.StockTakeItem).ThenInclude(ti => ti!.StockTake)
                .Include(m => m.StockTransfer)
                .Where(m => m.ProductId == productId.Value
                    && (locationId == null || m.StockLocationId == locationId.Value)
                    && (m.PurchaseItem == null || !m.PurchaseItem.Purchase!.IsDeleted)
                    && (m.SaleItem == null || (!m.SaleItem.Sale!.IsDeleted && m.SaleItem.Sale.Status != SaleStatus.Voided))
                    && (m.StockAdjustmentItem == null || !m.StockAdjustmentItem.StockAdjustment!.IsVoided)
                    && (m.StockTakeItem == null || !m.StockTakeItem.StockTake!.IsVoided)
                    && (m.StockTransfer == null || !m.StockTransfer.IsVoided));

            var zone = AccountingCalendar.Zone(configuration);
            DateOnly ToDate(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

            var priorMovements = query.Where(m => m.OccurredAt < range.FromUtc);
            var openingQty = await priorMovements.Select(m => (decimal?)m.Quantity).SumAsync(ct) ?? 0m;

            var inRangeQuery = query.Where(m => m.OccurredAt >= range.FromUtc && m.OccurredAt < range.ToUtc);
            var inQty = await inRangeQuery.Where(m => m.Quantity > 0).Select(m => (decimal?)m.Quantity).SumAsync(ct) ?? 0m;
            var outQty = -(await inRangeQuery.Where(m => m.Quantity < 0).Select(m => (decimal?)m.Quantity).SumAsync(ct) ?? 0m);
            var closingQty = openingQty + inQty - outQty;

            var allRangeMovements = await inRangeQuery
                .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
                .Select(m => new
                {
                    m.Id,
                    m.OccurredAt,
                    m.Type,
                    m.Quantity,
                    m.UnitCost,
                    LocationName = m.StockLocation != null ? m.StockLocation.Name : "Default",
                    m.Notes,
                    PurchaseId = m.PurchaseItem != null ? (int?)m.PurchaseItem.PurchaseId : null,
                    SaleId = m.SaleItem != null ? (int?)m.SaleItem.SaleId : null,
                    AdjustmentId = m.StockAdjustmentItem != null ? (int?)m.StockAdjustmentItem.StockAdjustmentId : null,
                    StockTransferId = m.StockTransferId
                })
                .ToListAsync(ct);

            var totalCount = allRangeMovements.Count;
            var currentRunning = openingQty;
            var allRows = new List<StockStatementRow>();

            foreach (var m in allRangeMovements)
            {
                currentRunning += m.Quantity;
                string typeLabel = m.Type switch
                {
                    InventoryMovementType.Purchase => "Purchase Received",
                    InventoryMovementType.PurchaseReturn => "Purchase Return",
                    InventoryMovementType.Sale => "Sale",
                    InventoryMovementType.SaleReturn => "Customer Return",
                    InventoryMovementType.Adjustment => "Stock Adjustment",
                    InventoryMovementType.TransferIn => "Transfer In",
                    InventoryMovementType.TransferOut => "Transfer Out",
                    InventoryMovementType.Opening => "Opening Stock",
                    InventoryMovementType.TransactionReversal => "Reversal",
                    _ => m.Type.ToString()
                };

                string reference = m.PurchaseId != null ? $"Purchase #{m.PurchaseId}"
                    : m.SaleId != null ? $"Sale #{m.SaleId}"
                    : m.AdjustmentId != null ? $"Adjustment #{m.AdjustmentId}"
                    : m.StockTransferId != null ? $"Transfer #{m.StockTransferId}"
                    : $"Movement #{m.Id}";

                string? referenceUrl = m.PurchaseId != null ? $"/purchases"
                    : m.SaleId != null ? $"/sales/{m.SaleId}"
                    : m.AdjustmentId != null ? $"/inventory/adjustments/{m.AdjustmentId}"
                    : m.StockTransferId != null ? $"/inventory/transfers"
                    : null;

                allRows.Add(new StockStatementRow(
                    ToDate(m.OccurredAt),
                    m.OccurredAt,
                    typeLabel,
                    reference,
                    referenceUrl,
                    m.LocationName,
                    m.Quantity,
                    currentRunning,
                    m.UnitCost,
                    decimal.Round(m.Quantity * m.UnitCost, 2, MidpointRounding.AwayFromZero),
                    m.Notes));
            }

            var limit = Math.Clamp(pageSize, 1, 1000);
            var totalPages = Math.Max(1, (totalCount + limit - 1) / limit);
            var currentPage = Math.Clamp(page, 1, totalPages);
            var pagedRows = allRows.Skip((currentPage - 1) * limit).Take(limit).ToList();

            string? selectedLocationName = locationId.HasValue
                ? availableLocations.FirstOrDefault(l => l.Id == locationId.Value)?.Name
                : null;

            return new StockStatement(
                product.Id,
                product.Name,
                product.Barcode,
                product.Unit,
                locationId,
                selectedLocationName,
                range,
                openingQty,
                inQty,
                outQty,
                closingQty,
                product.CostPrice,
                decimal.Round(closingQty * product.CostPrice, 2, MidpointRounding.AwayFromZero),
                pagedRows,
                totalCount,
                currentPage,
                availableProducts,
                availableLocations);
        }, ct);
    }

    private static string Label(Entry row) => row.Reconciliation
        ? row.Amount >= 0 ? "Cash count overage" : "Cash count shortage"
        : row.Kind switch
        {
            CashMovementKind.Sale when row.Collection => "Customer due collection",
            CashMovementKind.Sale => row.Amount >= 0 ? "Sale receipt" : "Sales refund",
            CashMovementKind.SupplierPayment => row.Amount < 0 ? "Supplier payment" : "Supplier refund",
            CashMovementKind.Expense => "Expense",
            CashMovementKind.Payroll => "Salary / bonus / advance payment",
            CashMovementKind.OwnerContribution => "Owner contribution",
            CashMovementKind.OwnerWithdrawal => "Owner withdrawal",
            CashMovementKind.CashIn => "Balance adjustment / cash added",
            CashMovementKind.Payout => row.Amount >= 0 ? "Card / wallet settlement payout" : "Payout settlement reversal",
            CashMovementKind.Reversal when row.Notes.Contains("clearing", StringComparison.OrdinalIgnoreCase) || row.Notes.Contains("payout", StringComparison.OrdinalIgnoreCase) => "Payout settlement reversal",
            CashMovementKind.OpeningBalance => "Opening balance",
            CashMovementKind.Transfer => row.Amount >= 0 ? "Transfer in" : "Transfer out",
            CashMovementKind.DebtBorrowing => "Money borrowed",
            CashMovementKind.DebtRepayment => "Debt repayment",
            CashMovementKind.Variance => row.Amount >= 0 ? "Cash count overage" : "Cash count shortage",
            _ => "Transaction reversal"
        };

    private sealed class Entry
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }
        public DateTimeOffset At { get; set; }
        public int Priority { get; set; }
        public CashMovementKind Kind { get; set; }
        public decimal Amount { get; set; }
        public string Notes { get; set; } = "";
        public string Reference { get; set; } = "";
        public bool Reconciliation { get; set; }
        public bool Collection { get; set; }
    }
}

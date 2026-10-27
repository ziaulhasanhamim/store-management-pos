using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class ReceivingReportService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<ReceivingActivityReport> ReadAsync(ClaimsPrincipal actor, string? period,
        DateOnly? start, DateOnly? end, int? locationId = null, int page = 1, CancellationToken ct = default, string? search = null, string? sort = null, int pageSize = 30)
    {
        return await StoreOperation.RunAsync<ReceivingActivityReport>(async () =>
        {
            var limit = Math.Clamp(pageSize, 1, 10000);
            var range = ReportService.ResolveRange(period, start, end, AccountingCalendar.Zone(configuration), DateTimeOffset.UtcNow);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            var locations = await db.StockLocations.AsNoTracking().TagWith("ReceivingReport.Locations").OrderBy(location => location.Name)
                .Select(location => new Lookup(location.Id, location.Name)).ToArrayAsync(ct);
            if (locationId is not null && !locations.Any(location => location.Id == locationId))
            {
                throw new ValidationException("Choose an existing stock location.");
            }

            var term = (search ?? "").Trim().ToLowerInvariant();
            var normalizedTerm = NameNormalization.Normalize(term);
            var numericId = int.TryParse(term.TrimStart('#'), out var parsed) ? parsed : -1;

            var purchasesQuery = db.Purchases.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Status == PurchaseStatus.Received
                    && p.ReceivedAt >= range.FromUtc && p.ReceivedAt < range.ToUtc
                    && (locationId == null || p.StockLocationId == locationId));

            if (term.Length > 0)
            {
                purchasesQuery = purchasesQuery.Where(p => p.Id == numericId
                    || p.Supplier!.NormalizedName.Contains(normalizedTerm)
                    || p.StockLocation!.NormalizedName.Contains(normalizedTerm)
                    || (p.PurchaseNumber != null && p.PurchaseNumber.ToLower().Contains(term))
                    || (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.ToLower().Contains(term))
                    || p.Items.Any(i => i.ProductName.ToLower().Contains(term)
                        || (i.Product != null && i.Product.Barcode != null && i.Product.Barcode.ToLower().Contains(term))));
            }

            var purchaseTransactions = purchasesQuery.Select(p => new TransactionActivity
            {
                Id = p.Id,
                IsTransfer = false,
                IsInbound = true,
                At = p.ReceivedAt!.Value,
                Source = p.Supplier!.Name,
                Destination = p.StockLocation!.Name,
                SourceNormalized = p.Supplier.NormalizedName,
                DestinationNormalized = p.StockLocation.NormalizedName,
                Quantity = p.Items.Select(i => (decimal?)i.Quantity).Sum() ?? 0,
                Value = p.TotalAmount,
                Number = p.PurchaseNumber,
                Paid = p.Payments.Select(pm => (decimal?)pm.Amount).Sum() ?? 0,
                Unpaid = p.TotalAmount - (p.Payments.Select(pm => (decimal?)pm.Amount).Sum() ?? 0)
            });

            var transfersQuery = db.StockTransfers.AsNoTracking()
                .Where(t => t.OccurredAt >= range.FromUtc && t.OccurredAt < range.ToUtc);

            if (term.Length > 0)
            {
                transfersQuery = transfersQuery.Where(t => t.Id == numericId
                    || t.SourceLocation!.NormalizedName.Contains(normalizedTerm)
                    || t.DestinationLocation!.NormalizedName.Contains(normalizedTerm)
                    || t.Items.Any(i => i.Product!.NormalizedName.Contains(normalizedTerm)
                        || (i.Product.Barcode != null && i.Product.Barcode.ToLower().Contains(term))));
            }

            IQueryable<TransactionActivity> transferTransactions;
            if (locationId == null)
            {
                // All locations: show both inbound and outbound sides
                var inboundTransfers = transfersQuery.Select(t => new TransactionActivity
                {
                    Id = t.Id,
                    IsTransfer = true,
                    IsInbound = true,
                    At = t.OccurredAt,
                    Source = t.SourceLocation!.Name,
                    Destination = t.DestinationLocation!.Name,
                    SourceNormalized = t.SourceLocation.NormalizedName,
                    DestinationNormalized = t.DestinationLocation.NormalizedName,
                    Quantity = t.Items.Select(i => (decimal?)i.Quantity).Sum() ?? 0,
                    Value = t.Items.Select(i => (decimal?)(i.Quantity * i.UnitCost)).Sum() ?? 0,
                    Number = null,
                    Paid = 0,
                    Unpaid = 0
                });

                var outboundTransfers = transfersQuery.Select(t => new TransactionActivity
                {
                    Id = t.Id,
                    IsTransfer = true,
                    IsInbound = false,
                    At = t.OccurredAt,
                    Source = t.SourceLocation!.Name,
                    Destination = t.DestinationLocation!.Name,
                    SourceNormalized = t.SourceLocation.NormalizedName,
                    DestinationNormalized = t.DestinationLocation.NormalizedName,
                    Quantity = -(t.Items.Select(i => (decimal?)i.Quantity).Sum() ?? 0),
                    Value = -(t.Items.Select(i => (decimal?)(i.Quantity * i.UnitCost)).Sum() ?? 0),
                    Number = null,
                    Paid = 0,
                    Unpaid = 0
                });

                transferTransactions = inboundTransfers.Concat(outboundTransfers);
            }
            else
            {
                // Single location: show inbound if destination matches, outbound if source matches
                var inboundTransfers = transfersQuery.Where(t => t.DestinationLocationId == locationId)
                    .Select(t => new TransactionActivity
                    {
                        Id = t.Id,
                        IsTransfer = true,
                        IsInbound = true,
                        At = t.OccurredAt,
                        Source = t.SourceLocation!.Name,
                        Destination = t.DestinationLocation!.Name,
                        SourceNormalized = t.SourceLocation.NormalizedName,
                        DestinationNormalized = t.DestinationLocation.NormalizedName,
                        Quantity = t.Items.Select(i => (decimal?)i.Quantity).Sum() ?? 0,
                        Value = t.Items.Select(i => (decimal?)(i.Quantity * i.UnitCost)).Sum() ?? 0,
                        Number = null,
                        Paid = 0,
                        Unpaid = 0
                    });

                var outboundTransfers = transfersQuery.Where(t => t.SourceLocationId == locationId)
                    .Select(t => new TransactionActivity
                    {
                        Id = t.Id,
                        IsTransfer = true,
                        IsInbound = false,
                        At = t.OccurredAt,
                        Source = t.SourceLocation!.Name,
                        Destination = t.DestinationLocation!.Name,
                        SourceNormalized = t.SourceLocation.NormalizedName,
                        DestinationNormalized = t.DestinationLocation.NormalizedName,
                        Quantity = -(t.Items.Select(i => (decimal?)i.Quantity).Sum() ?? 0),
                        Value = -(t.Items.Select(i => (decimal?)(i.Quantity * i.UnitCost)).Sum() ?? 0),
                        Number = null,
                        Paid = 0,
                        Unpaid = 0
                    });

                transferTransactions = inboundTransfers.Concat(outboundTransfers);
            }

            var allTransactions = purchaseTransactions.Concat(transferTransactions);
            var count = await allTransactions.CountAsync(ct);

            // Metrics across entire filtered date range
            var purchaseValue = await purchaseTransactions.Select(t => (decimal?)t.Value).SumAsync(ct) ?? 0;
            var transferIn = await transferTransactions.Where(t => t.IsInbound).Select(t => (decimal?)t.Value).SumAsync(ct) ?? 0;
            var transferOut = -(await transferTransactions.Where(t => !t.IsInbound).Select(t => (decimal?)t.Value).SumAsync(ct) ?? 0);
            var supplierPaid = await purchaseTransactions.Select(t => (decimal?)t.Paid).SumAsync(ct) ?? 0;
            var supplierUnpaid = purchaseValue - supplierPaid;

            var current = Math.Clamp(page, 1, Math.Max(1, (count + limit - 1) / limit));

            var ordered = (string.IsNullOrWhiteSpace(sort) ? "id" : sort) switch
            {
                "id" => allTransactions.OrderBy(t => t.Id),
                "id-desc" => allTransactions.OrderByDescending(t => t.Id),
                "date" => allTransactions.OrderBy(t => t.At),
                "date-desc" => allTransactions.OrderByDescending(t => t.At),
                "source" => allTransactions.OrderBy(t => t.Source).ThenBy(t => t.Destination),
                "source-desc" => allTransactions.OrderByDescending(t => t.Source).ThenByDescending(t => t.Destination),
                "quantity" => allTransactions.OrderBy(t => t.Quantity),
                "quantity-desc" => allTransactions.OrderByDescending(t => t.Quantity),
                "value" => allTransactions.OrderBy(t => t.Value),
                "value-desc" => allTransactions.OrderByDescending(t => t.Value),
                _ => allTransactions.OrderBy(t => t.Id)
            };

            var pagedRows = await ordered
                .ThenBy(t => t.IsTransfer)
                .ThenByDescending(t => t.Id)
                .ThenBy(t => t.IsInbound ? 0 : 1)
                .Skip((current - 1) * limit)
                .Take(limit)
                .ToListAsync(ct);

            // Query lines ONLY for the paged transactions
            var pagePurchaseIds = pagedRows.Where(r => !r.IsTransfer).Select(r => r.Id).Distinct().ToArray();
            var pageTransferIds = pagedRows.Where(r => r.IsTransfer).Select(r => r.Id).Distinct().ToArray();

            var purchaseItemsLookup = new Dictionary<int, List<ReceivingActivityLine>>();
            if (pagePurchaseIds.Length > 0)
            {
                var pItems = await db.PurchaseItems.AsNoTracking()
                    .Where(i => pagePurchaseIds.Contains(i.PurchaseId))
                    .Select(i => new
                    {
                        i.PurchaseId,
                        i.ProductId,
                        Product = i.ProductName,
                        Barcode = i.Product != null ? i.Product.Barcode : null,
                        i.Unit,
                        i.Quantity,
                        i.UnitCost,
                        Value = i.Quantity * i.UnitCost
                    })
                    .ToListAsync(ct);

                foreach (var item in pItems)
                {
                    if (!purchaseItemsLookup.TryGetValue(item.PurchaseId, out var list))
                    {
                        list = [];
                        purchaseItemsLookup[item.PurchaseId] = list;
                    }
                    list.Add(new ReceivingActivityLine(item.ProductId, item.Product, item.Barcode, item.Unit, item.Quantity, item.UnitCost, item.Value));
                }
            }

            var transferItemsLookup = new Dictionary<int, List<ReceivingActivityLine>>();
            if (pageTransferIds.Length > 0)
            {
                var tItems = await db.StockTransferItems.AsNoTracking()
                    .Where(i => pageTransferIds.Contains(i.StockTransferId))
                    .Select(i => new
                    {
                        i.StockTransferId,
                        i.ProductId,
                        Product = i.Product!.Name,
                        Barcode = i.Product.Barcode,
                        i.Product.Unit,
                        i.Quantity,
                        i.UnitCost,
                        Value = i.Quantity * i.UnitCost
                    })
                    .ToListAsync(ct);

                foreach (var item in tItems)
                {
                    if (!transferItemsLookup.TryGetValue(item.StockTransferId, out var list))
                    {
                        list = [];
                        transferItemsLookup[item.StockTransferId] = list;
                    }
                    list.Add(new ReceivingActivityLine(item.ProductId, item.Product, item.Barcode, item.Unit, item.Quantity, item.UnitCost, item.Value));
                }
            }

            var rows = pagedRows.Select(t =>
            {
                IReadOnlyList<ReceivingActivityLine> items;
                if (!t.IsTransfer)
                {
                    items = purchaseItemsLookup.GetValueOrDefault(t.Id, []);
                }
                else
                {
                    var rawItems = transferItemsLookup.GetValueOrDefault(t.Id, []);
                    if (!t.IsInbound)
                    {
                        // Invert signs for outbound transfer lines
                        items = rawItems.Select(i => i with { Quantity = -i.Quantity, Value = -i.Value }).ToArray();
                    }
                    else
                    {
                        items = rawItems;
                    }
                }

                return new ReceivingActivityRow(
                    t.Id,
                    t.IsTransfer,
                    t.At,
                    t.Source,
                    t.Destination,
                    t.Quantity,
                    t.Value,
                    items,
                    t.Number,
                    t.Paid,
                    t.Unpaid);
            }).ToArray();

            return new ReceivingActivityReport(
                rows,
                range,
                locations,
                count,
                current,
                purchaseValue,
                transferIn,
                transferOut,
                supplierPaid,
                supplierUnpaid);
        }, ct);
    }

    private sealed class TransactionActivity
    {
        public int Id { get; set; }
        public bool IsTransfer { get; set; }
        public bool IsInbound { get; set; }
        public DateTimeOffset At { get; set; }
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public string SourceNormalized { get; set; } = "";
        public string DestinationNormalized { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Value { get; set; }
        public string? Number { get; set; }
        public decimal Paid { get; set; }
        public decimal Unpaid { get; set; }
    }
}

using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

internal static class FinancePositionReader
{
    public static async Task<(decimal SupplierDue, decimal CustomerDue)> DuesAtAsync(
        AppDbContext db, DateTimeOffset before, CancellationToken ct)
    {
        // Stored balances include imported opening dues. Undo later operational postings rather
        // than subtracting the initial opening balance.
        var suppliers = await db.SupplierBalances.AsNoTracking()
            .Where(balance => balance.OpenedAt == null || balance.OpenedAt < before
                || balance.Entries.Any(entry => entry.OccurredAt < before || entry.Type == BalanceEntryType.OpeningBalance))
            .Select(balance => balance.CurrentBalance
                - (balance.Entries.Where(entry => entry.OccurredAt >= before && entry.Type != BalanceEntryType.OpeningBalance)
                    .Select(entry => (decimal?)entry.Amount).Sum() ?? 0))
            .ToArrayAsync(ct);
        var customers = await db.CustomerBalances.AsNoTracking()
            .Where(balance => balance.OpenedAt == null || balance.OpenedAt < before
                || balance.Entries.Any(entry => entry.OccurredAt < before || entry.Type == BalanceEntryType.OpeningBalance))
            .Select(balance => balance.CurrentBalance
                - (balance.Entries.Where(entry => entry.OccurredAt >= before && entry.Type != BalanceEntryType.OpeningBalance)
                    .Select(entry => (decimal?)entry.Amount).Sum() ?? 0))
            .ToArrayAsync(ct);

        return (suppliers.Sum(), customers.Sum());
    }

    public static async Task<decimal> StockValueAtAsync(
        AppDbContext db, DateTimeOffset before, CancellationToken ct, int? locationId = null)
    {
        var products = await db.Products.AsNoTracking()
            .Select(product => new
            {
                product.Id,
                product.CostPrice,
                Quantity = product.InventoryBalances
                    .Where(balance => locationId == null || balance.StockLocationId == locationId.Value)
                    .Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0
            })
            .ToArrayAsync(ct);
        var laterQuantities = await db.InventoryMovements.AsNoTracking()
            .Where(move => move.OccurredAt >= before && (locationId == null || move.StockLocationId == locationId.Value))
            .GroupBy(move => move.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(move => move.Quantity) })
            .ToDictionaryAsync(row => row.ProductId, row => row.Quantity, ct);

        // Latest stock uses the same quantity × average cost as the inventory report,
        // across every location. Reversals and transfers must also undo their quantity
        // effects when viewing a date before those movements.
        var costs = products.ToDictionary(product => product.Id, product => product.CostPrice);
        var activeMovements = db.InventoryMovements.AsNoTracking()
            .Where(move => move.Type != InventoryMovementType.TransactionReversal
                && (move.PurchaseItem == null || !move.PurchaseItem.Purchase!.IsDeleted)
                && (move.SaleItem == null || !move.SaleItem.Sale!.IsDeleted)
                && (locationId == null || move.StockLocationId == locationId.Value));
        var revaluedIds = await activeMovements
            .Where(move => move.OccurredAt >= before && move.Quantity > 0
                && (move.Type == InventoryMovementType.Opening
                    || move.Type == InventoryMovementType.Purchase
                    || move.Type == InventoryMovementType.Adjustment))
            .Select(move => move.ProductId)
            .Distinct()
            .ToArrayAsync(ct);

        if (revaluedIds.Length > 0)
        {
            var history = await activeMovements.Where(move => revaluedIds.Contains(move.ProductId))
                .OrderBy(move => move.Id)
                .Select(move => new { move.ProductId, move.OccurredAt, move.Type, move.Quantity, move.UnitCost })
                .ToArrayAsync(ct);

            foreach (var group in history.GroupBy(move => move.ProductId))
            {
                var product = products.Single(product => product.Id == group.Key);
                var quantity = product.Quantity - group.Sum(move => move.Quantity);
                var cost = quantity == 0 ? 0 : product.CostPrice;

                foreach (var move in group.Where(move => move.OccurredAt < before))
                {
                    if (move.Quantity > 0 && quantity + move.Quantity > 0
                        && move.Type is InventoryMovementType.Opening or InventoryMovementType.Purchase or InventoryMovementType.Adjustment)
                    {
                        cost = decimal.Round(cost + (move.UnitCost - cost)
                            * (move.Quantity / (quantity + move.Quantity)), 2, MidpointRounding.AwayFromZero);
                    }

                    quantity += move.Quantity;
                }

                costs[group.Key] = cost;
            }
        }

        return products.Sum(product =>
            (product.Quantity - laterQuantities.GetValueOrDefault(product.Id)) * costs[product.Id]);
    }
}

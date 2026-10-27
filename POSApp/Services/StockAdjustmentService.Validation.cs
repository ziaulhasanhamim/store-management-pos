using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed partial class StockAdjustmentService
{
    private static async Task ValidateLocationAsync(AppDbContext db, int locationId, CancellationToken ct)
    {
        if (!await db.StockLocations.AnyAsync(location => location.Id == locationId && !location.IsDeleted, ct))
        {
            throw new ValidationException("Choose an active store or warehouse.");
        }
    }

    private static void ValidateEntries(IReadOnlyList<StockAdjustmentEntry>? entries)
    {
        if (entries is null || entries.Count is < 1 or > 10000
            || entries.Any(item => item is null || item.ProductId <= 0
                || item.Quantity < -MaximumQuantity || item.Quantity > MaximumQuantity
                || decimal.Round(item.Quantity, 3) != item.Quantity)
            || entries.Select(item => item.ProductId).Distinct().Count() != entries.Count)
        {
            throw new ValidationException("Add 1–10,000 different products with quantities from -999,999,999,999,999.999 to 999,999,999,999,999.999, including zero, and up to three decimal places.");
        }
        decimal total = 0;
        foreach (var entry in entries)
        {
            StoreAccess.Money(entry.UnitCost, "Unit cost");
            if (entry.UnitCost > 0 && Math.Abs(entry.Quantity) > MaximumMoney / entry.UnitCost)
            {
                throw new ValidationException("An item's stock value exceeds the supported range.");
            }
            var value = decimal.Round(entry.Quantity * entry.UnitCost, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(total + value) > MaximumMoney)
            {
                throw new ValidationException("The stock adjustment total exceeds the supported range.");
            }
            total += value;
        }
    }

    private static async Task<Dictionary<int, Product>> ValidateProductsAsync(
        AppDbContext db, int locationId, IReadOnlyList<StockAdjustmentEntry> entries, CancellationToken ct)
    {
        var ids = entries.Select(item => item.ProductId).ToArray();
        var products = await db.Products.Where(product => ids.Contains(product.Id) && !product.IsDeleted)
            .ToDictionaryAsync(product => product.Id, ct);
        if (products.Count != ids.Length)
        {
            throw new ValidationException("Some product IDs were not found. Create the products first, then add stock.");
        }
        foreach (var entry in entries)
        {
            var product = products[entry.ProductId];
            if (product.IsNonStocked)
            {
                throw new ValidationException($"{product.Name} is non-stocked and cannot have stock adjustment.");
            }
            if (!product.AllowFractionUnit && decimal.Truncate(entry.Quantity) != entry.Quantity)
            {
                throw new ValidationException($"{product.Name} only allows whole quantities.");
            }

        }
        return products;
    }

}

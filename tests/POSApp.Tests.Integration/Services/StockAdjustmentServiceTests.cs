using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class StockAdjustmentServiceTests : PosIntegrationTestBase
{
    private async Task<int> CreateTestProductAsync(decimal cost, decimal price)
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();

        return await inventory.SaveProductAsync(InventoryUser, null, new ProductInput
        {
            Name = $"Adjustment Item {Guid.NewGuid():N}",
            Unit = "piece",
            CostPrice = cost,
            SellingPrice = price,
            SupplierId = 1,
            CategoryId = 1
        });
    }

    [Test]
    public async Task CreateAsync_PositiveQuantity_IncrementsStock_AndUpdatesInventoryBalance()
    {
        var productId = await CreateTestProductAsync(cost: 40m, price: 80m);

        await using var scope = CreateScope();
        var adjustmentService = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var command = new StockAdjustmentCommand(
            RequestId: Guid.NewGuid(),
            LocationId: 1,
            Date: AccountingCalendar.Today(null),
            Notes: "Initial inventory count / found items",
            Items:
            [
                new StockAdjustmentEntry(productId, Quantity: 10m, UnitCost: 40m)
                {
                    Reason = StockAdjustmentReason.OpeningOrFound
                }
            ]);

        var result = await adjustmentService.CreateAsync(AdminUser, command);
        await result.Should().NotBeNull();
        await result.Id.Should().BeGreaterThan(0);
        await result.ItemCount.Should().BeEqualTo(1);

        // Verify balance in DB
        var balance = await db.InventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
        await balance.Should().NotBeNull();
        await balance!.QuantityOnHand.Should().BeEqualTo(10m);

        // Verify movement
        var movement = await db.InventoryMovements.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProductId == productId && m.Type == InventoryMovementType.Adjustment);
        await movement.Should().NotBeNull();
        await movement!.Quantity.Should().BeEqualTo(10m);
    }

    [Test]
    public async Task CreateAsync_DamageOrWaste_DecrementsStock_AndLogsReasonLabel()
    {
        var productId = await CreateTestProductAsync(cost: 25m, price: 50m);

        // Seed 15 units of stock first
        await using (var scope = CreateScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
            await inventory.ReceiveAsync(InventoryUser, new ReceiveCommand(
                RequestId: Guid.NewGuid(),
                SupplierId: 1,
                LocationId: 1,
                Lines: [new ReceiveLine(productId, Quantity: 15m, UnitCost: 25m)],
                Payment: 375m,
                Method: PaymentMethod.Cash,
                Invoice: "PO-SETUP-001",
                Notes: "Stock setup"));
        }

        // Apply -3 damaged units adjustment
        await using (var scope = CreateScope())
        {
            var adjustmentService = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var command = new StockAdjustmentCommand(
                RequestId: Guid.NewGuid(),
                LocationId: 1,
                Date: AccountingCalendar.Today(null),
                Notes: "Broken in transit",
                Items:
                [
                    new StockAdjustmentEntry(productId, Quantity: -3m, UnitCost: 25m)
                    {
                        Reason = StockAdjustmentReason.Damaged
                    }
                ]);

            var result = await adjustmentService.CreateAsync(AdminUser, command);
            await result.Should().NotBeNull();

            // Balance should be reduced from 15 to 12
            var balance = await db.InventoryBalances.AsNoTracking()
                .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
            await balance.Should().NotBeNull();
            await balance!.QuantityOnHand.Should().BeEqualTo(12m);

            // Movement must record negative quantity with reason label in Notes
            var movement = await db.InventoryMovements.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProductId == productId && m.Type == InventoryMovementType.Adjustment && m.Quantity == -3m);
            await movement.Should().NotBeNull();
            await (movement!.Notes?.Contains("Damaged / Broken") == true).Should().BeTrue();
        }
    }

    [Test]
    public async Task CreateAsync_DuplicateRequestId_ReturnsExistingResultWithoutDuplicateEffects()
    {
        var productId = await CreateTestProductAsync(cost: 30m, price: 60m);
        var requestId = Guid.NewGuid();

        var command = new StockAdjustmentCommand(
            RequestId: requestId,
            LocationId: 1,
            Date: AccountingCalendar.Today(null),
            Notes: "Idempotency test adjustment",
            Items:
            [
                new StockAdjustmentEntry(productId, Quantity: 5m, UnitCost: 30m)
                {
                    Reason = StockAdjustmentReason.OpeningOrFound
                }
            ]);

        await using (var scope = CreateScope())
        {
            var adjustmentService = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
            var firstResult = await adjustmentService.CreateAsync(AdminUser, command);
            await firstResult.Should().NotBeNull();
        }

        // Re-post same request with identical parameters
        await using (var scope = CreateScope())
        {
            var adjustmentService = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var secondResult = await adjustmentService.CreateAsync(AdminUser, command);
            await secondResult.Should().NotBeNull();

            // Stock should still be exactly 5 (NOT 10)
            var balance = await db.InventoryBalances.AsNoTracking()
                .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
            await balance!.QuantityOnHand.Should().BeEqualTo(5m);
        }
    }
}

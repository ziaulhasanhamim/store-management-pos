using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class InventoryServiceTests : PosIntegrationTestBase
{
    [Test]
    public async Task SaveProductAsync_WhenValid_PersistsProductWithGeneratedBarcode()
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var input = new ProductInput
        {
            Name = "Mechanical Keyboard Blue Switch",
            Unit = "piece",
            CostPrice = 45m,
            SellingPrice = 89.99m,
            SupplierId = 1,
            CategoryId = 1,
            GenerateBarcodeWhenEmpty = true
        };

        var productId = await inventory.SaveProductAsync(InventoryUser, null, input);
        await productId.Should().BeGreaterThan(0);

        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId);
        await product.Should().NotBeNull();
        await product!.Name.Should().BeEqualTo("Mechanical Keyboard Blue Switch");
        await string.IsNullOrWhiteSpace(product.Barcode).Should().BeFalse();
        await product.CostPrice.Should().BeEqualTo(45m);
        await product.SellingPrice.Should().BeEqualTo(89.99m);
    }

    [Test]
    public async Task ReceiveAsync_IncreasesStock_AndCalculatesMovingAverageCost()
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Create a product with initial cost = 100
        var productInput = new ProductInput
        {
            Name = "Ergonomic Office Chair",
            Unit = "piece",
            CostPrice = 100m,
            SellingPrice = 250m,
            SupplierId = 1,
            CategoryId = 1
        };
        var productId = await inventory.SaveProductAsync(InventoryUser, null, productInput);

        // 2. First receiving: 10 units @ 100
        var receive1 = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(productId, Quantity: 10, UnitCost: 100m)],
            Payment: 1000m,
            Method: PaymentMethod.Cash,
            Invoice: "PO-CHAIR-001",
            Notes: "Batch 1");

        var result1 = await inventory.ReceiveAsync(InventoryUser, receive1);
        await result1.Should().NotBeNull();

        var balance1 = await db.InventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
        await balance1.Should().NotBeNull();
        await balance1!.QuantityOnHand.Should().BeEqualTo(10);

        var productAfter1 = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        await productAfter1.CostPrice.Should().BeEqualTo(100m);

        // 3. Second receiving: 10 units @ 200 (Total stock will be 20, weighted average: (10*100 + 10*200) / 20 = 150)
        var receive2 = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(productId, Quantity: 10, UnitCost: 200m)],
            Payment: 2000m,
            Method: PaymentMethod.Cash,
            Invoice: "PO-CHAIR-002",
            Notes: "Batch 2");

        var result2 = await inventory.ReceiveAsync(InventoryUser, receive2);
        await result2.Should().NotBeNull();

        var balance2 = await db.InventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
        await balance2!.QuantityOnHand.Should().BeEqualTo(20);

        var productAfter2 = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        await productAfter2.CostPrice.Should().BeEqualTo(150m);
    }

    [Test]
    public async Task ReceiveAsync_WithCredit_UpdatesSupplierDueAndBalanceEntries()
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var productInput = new ProductInput
        {
            Name = "USB-C Hub Multiport",
            Unit = "piece",
            CostPrice = 30m,
            SellingPrice = 60m,
            SupplierId = 1,
            CategoryId = 1
        };
        var productId = await inventory.SaveProductAsync(InventoryUser, null, productInput);

        // Total purchase amount = 10 * 30 = 300, payment = 100 -> Unpaid credit = 200
        var receiveCommand = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(productId, Quantity: 10, UnitCost: 30m)],
            Payment: 100m,
            Method: PaymentMethod.Cash,
            Invoice: "PO-HUB-001",
            Notes: "Partial payment receipt");

        await inventory.ReceiveAsync(InventoryUser, receiveCommand);

        var supplierBalance = await db.SupplierBalances.AsNoTracking()
            .SingleOrDefaultAsync(sb => sb.SupplierId == 1);
        await supplierBalance.Should().NotBeNull();
        await supplierBalance!.CurrentBalance.Should().BeEqualTo(200m);

        var balanceEntries = await db.SupplierBalanceEntries.AsNoTracking()
            .Where(e => e.SupplierBalanceId == supplierBalance.Id)
            .ToListAsync();
        await balanceEntries.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ListAsync_WithLocationFilter_ReturnsStockScopedToLocation()
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();

        // 1. Create a second location
        var loc2Id = await inventory.AddLocationAsync(AdminUser, "Warehouse South", canSell: true);
        await loc2Id.Should().BeGreaterThan(1);

        // 2. Create product
        var productInput = new ProductInput
        {
            Name = "Ultra HD Web Camera",
            Unit = "piece",
            CostPrice = 75m,
            SellingPrice = 130m,
            SupplierId = 1,
            CategoryId = 1
        };
        var productId = await inventory.SaveProductAsync(InventoryUser, null, productInput);

        // 3. Receive stock only in Location 1
        var receive = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(productId, Quantity: 15, UnitCost: 75m)],
            Payment: 1125m,
            Method: PaymentMethod.Cash,
            Invoice: "PO-CAM-001",
            Notes: "Location 1 stock");
        await inventory.ReceiveAsync(InventoryUser, receive);

        // 4. Assert stock in Location 1 is 15
        var loc1Items = await inventory.ListAsync(InventoryUser, search: "Ultra HD Web Camera", locationId: 1);
        var itemInLoc1 = loc1Items.FirstOrDefault(i => i.Id == productId);
        await itemInLoc1.Should().NotBeNull();
        await itemInLoc1!.Stock.Should().BeEqualTo(15);

        // 5. Assert stock in Location 2 is 0
        var loc2Items = await inventory.ListAsync(InventoryUser, search: "Ultra HD Web Camera", locationId: loc2Id);
        var itemInLoc2 = loc2Items.FirstOrDefault(i => i.Id == productId);
        await itemInLoc2.Should().NotBeNull();
        await itemInLoc2!.Stock.Should().BeEqualTo(0);
    }

    [Test]
    public async Task SaveProductAsync_CanChangeToNonStocked_WhenZeroQuantityAndNoSales()
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Create a stocked product
        var productInput = new ProductInput
        {
            Name = "Service Fee Placeholder",
            Unit = "piece",
            CostPrice = 0m,
            SellingPrice = 50m,
            SupplierId = 1,
            CategoryId = 1,
            IsNonStocked = false
        };
        var productId = await inventory.SaveProductAsync(InventoryUser, null, productInput);

        // Add a zero-quantity balance and zero-quantity movement
        db.InventoryBalances.Add(new Persistence.Entities.InventoryBalance
        {
            ProductId = productId,
            StockLocationId = 1,
            QuantityOnHand = 0m
        });
        db.InventoryMovements.Add(new Persistence.Entities.InventoryMovement
        {
            ProductId = productId,
            StockLocationId = 1,
            Quantity = 0m,
            UnitCost = 0m,
            Type = InventoryMovementType.Adjustment
        });
        await db.SaveChangesAsync();

        // 2. Change to non-stocked
        productInput.IsNonStocked = true;
        var updatedId = await inventory.SaveProductAsync(InventoryUser, productId, productInput);
        await updatedId.Should().BeEqualTo(productId);

        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        await product.IsNonStocked.Should().BeTrue();
        await product.CostPrice.Should().BeEqualTo(0m);

        var balances = await db.InventoryBalances.Where(b => b.ProductId == productId).ToListAsync();
        await balances.Count.Should().BeEqualTo(0);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class SalesServiceTests : PosIntegrationTestBase
{
    private async Task<int> SetupProductWithStockAsync(decimal cost, decimal price, decimal quantity)
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();

        var productId = await inventory.SaveProductAsync(InventoryUser, null, new ProductInput
        {
            Name = $"Test Product {Guid.NewGuid():N}",
            Unit = "piece",
            CostPrice = cost,
            SellingPrice = price,
            SupplierId = 1,
            CategoryId = 1
        });

        await inventory.ReceiveAsync(InventoryUser, new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(productId, quantity, cost)],
            Payment: cost * quantity,
            Method: PaymentMethod.Cash,
            Invoice: $"INV-{Guid.NewGuid():N}"[..12],
            Notes: "Setup stock"));

        return productId;
    }

    [Test]
    public async Task SaveAsync_CompleteSale_DecrementsStock_UpdatesCustomerBalance_AndRecordsPayment()
    {
        var productId = await SetupProductWithStockAsync(cost: 100m, price: 200m, quantity: 10m);

        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Total = 3 * 200 = 600. Payment = 400 (cash). Credit due = 200.
        var saleCommand = new SalesCommand(
            RequestId: Guid.NewGuid(),
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(productId, Quantity: 3, UnitPrice: 200m)],
            Discount: 0m,
            Payment: 400m,
            Method: PaymentMethod.Cash,
            Notes: "Direct service sale test");

        var result = await sales.SaveAsync(SalesUser, saleCommand, complete: true);
        await result.Should().NotBeNull();
        await result.Id.Should().BeGreaterThan(0);

        // 1. Verify stock decremented to 7
        var balance = await db.InventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
        await balance.Should().NotBeNull();
        await balance!.QuantityOnHand.Should().BeEqualTo(7m);

        // 2. Verify negative inventory movement logged
        var movement = await db.InventoryMovements.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProductId == productId && m.Type == InventoryMovementType.Sale);
        await movement.Should().NotBeNull();
        await movement!.Quantity.Should().BeEqualTo(-3m);

        // 3. Verify customer balance updated for unpaid credit (200)
        var customerBalance = await db.CustomerBalances.AsNoTracking()
            .SingleOrDefaultAsync(cb => cb.CustomerId == 1);
        await customerBalance.Should().NotBeNull();
        await customerBalance!.CurrentBalance.Should().BeEqualTo(200m);

        // 4. Verify cash movement recorded for payment (400)
        var cashMovements = await db.CashMovements.AsNoTracking().ToListAsync();
        var saleCash = cashMovements.FirstOrDefault(cm => cm.Amount == 400m);
        await saleCash.Should().NotBeNull();
    }

    [Test]
    public async Task SaveAsync_DraftSale_DoesNotDecrementStock_AndCanBeRetrieved()
    {
        var productId = await SetupProductWithStockAsync(cost: 50m, price: 100m, quantity: 5m);

        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var draftId = Guid.NewGuid();
        var saleCommand = new SalesCommand(
            RequestId: draftId,
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(productId, Quantity: 2, UnitPrice: 100m)],
            Discount: 0m,
            Payment: 0m,
            Method: PaymentMethod.Cash,
            Notes: "Draft for later");

        var result = await sales.SaveAsync(SalesUser, saleCommand, complete: false);
        await result.Should().NotBeNull();

        // Stock must still be 5
        var balance = await db.InventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
        await balance!.QuantityOnHand.Should().BeEqualTo(5m);

        // Retrieve draft
        var retrievedDraft = await sales.DraftAsync(SalesUser, draftId);
        await retrievedDraft.Should().NotBeNull();
        await retrievedDraft!.RequestId.Should().BeEqualTo(draftId);
        await retrievedDraft.Notes.Should().BeEqualTo("Draft for later");
        await retrievedDraft.Lines.Length.Should().BeEqualTo(1);
        await retrievedDraft.Lines[0].Quantity.Should().BeEqualTo(2m);
    }

    [Test]
    public async Task SaveAsync_WhenFractionalQuantityOnWholeUnitProduct_ThrowsValidationException()
    {
        var productId = await SetupProductWithStockAsync(cost: 30m, price: 60m, quantity: 10m);

        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();

        // Try selling 1.5 units when AllowFractionUnit is false (default for piece products)
        var saleCommand = new SalesCommand(
            RequestId: Guid.NewGuid(),
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(productId, Quantity: 1.5m, UnitPrice: 60m)],
            Discount: 0m,
            Payment: 90m,
            Method: PaymentMethod.Cash,
            Notes: "Should fail due to fractional quantity");

        await Assert.ThrowsAsync<ValidationException>(() => sales.SaveAsync(SalesUser, saleCommand, complete: true));
    }

    [Test]
    public async Task BillAsync_ReturnsCorrectSaleDetails_AndTracksVoidStatus()
    {
        var productId = await SetupProductWithStockAsync(cost: 40m, price: 80m, quantity: 10m);

        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var records = scope.ServiceProvider.GetRequiredService<RecordAdministration>();

        var saleCommand = new SalesCommand(
            RequestId: Guid.NewGuid(),
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(productId, Quantity: 2, UnitPrice: 80m)],
            Discount: 10m,
            Payment: 150m,
            Method: PaymentMethod.Cash,
            Notes: "Sale to verify bill and void");

        var saleResult = await sales.SaveAsync(SalesUser, saleCommand, complete: true);

        // 1. Check bill
        var bill = await sales.BillAsync(AdminUser, saleResult.Id);
        await bill.Should().NotBeNull();
        await bill!.Subtotal.Should().BeEqualTo(160m);
        await bill.Discount.Should().BeEqualTo(10m);
        await bill.Total.Should().BeEqualTo(150m);
        await bill.Paid.Should().BeEqualTo(150m);
        await bill.IsVoided.Should().BeFalse();

        // 2. Void sale via RecordAdministration
        var recordData = await records.FindAsync(AdminUser, "sale", saleResult.Id);
        await recordData.Should().NotBeNull();

        await records.ChangeAsync(AdminUser, "sale", saleResult.Id, new RecordCorrectionInput
        {
            PartyId = recordData!.PartyId,
            Reason = "Item damaged during packaging, sale canceled and voided",
            ExpectedRevision = recordData.Revision
        }, delete: true);

        // 3. Verify bill shows IsVoided = true
        var voidedBill = await sales.BillAsync(AdminUser, saleResult.Id);
        await voidedBill.Should().NotBeNull();
        await voidedBill!.IsVoided.Should().BeTrue();

        // 4. Verify stock was restored to 10
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var balance = await db.InventoryBalances.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ProductId == productId && b.StockLocationId == 1);
        await balance!.QuantityOnHand.Should().BeEqualTo(10m);
    }
}

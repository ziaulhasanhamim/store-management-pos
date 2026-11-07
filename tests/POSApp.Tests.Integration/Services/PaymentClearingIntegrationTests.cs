using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Integration.Services;

public class PaymentClearingIntegrationTests : PosIntegrationTestBase
{
    [Test]
    public async Task RecordPayout_CreatesGrossDepositAndFeeExpense_MaintainsExactBankStatementAndLocationAttribution()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var clearing = scope.ServiceProvider.GetRequiredService<PaymentClearingService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Setup product with stock at location 1 (Main Store)
        var productId = await inventory.SaveProductAsync(InventoryUser, null, new ProductInput
        {
            Name = $"Card Clearing Product {Guid.NewGuid():N}",
            Unit = "piece",
            CostPrice = 200m,
            SellingPrice = 850m,
            SupplierId = 1,
            CategoryId = 1
        });

        await inventory.ReceiveAsync(InventoryUser, new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(productId, Quantity: 5m, UnitCost: 200m)],
            Payment: 1000m,
            Method: PaymentMethod.BankTransfer,
            Invoice: "PO-CLEAR-001",
            Notes: "Stock for card clearing test"));

        // Get initial bank balance
        var bankAccount = await db.BankAccounts.SingleAsync(b => b.Code == "MAIN");
        var initialBankBalance = bankAccount.CurrentBalance;

        // 2. Complete sale with Card payment = 850 Tk
        var saleCommand = new SalesCommand(
            RequestId: Guid.NewGuid(),
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(productId, Quantity: 1m, UnitPrice: 850m)],
            Discount: 0m,
            Payment: 850m,
            Method: PaymentMethod.Card,
            Notes: "Sale paid by card");

        var saleResult = await sales.SaveAsync(SalesUser, saleCommand, complete: true);
        await saleResult.Should().NotBeNull();

        // 3. Verify PaymentClearingEntry was created and is Unsettled
        var clearingEntries = await db.PaymentClearingEntries.AsNoTracking()
            .Where(e => e.Payment!.SaleId == saleResult.Id)
            .ToListAsync();
        await clearingEntries.Count.Should().BeEqualTo(1);
        var entry = clearingEntries[0];
        await entry.Status.Should().BeEqualTo(PaymentClearingStatus.Unsettled);
        await entry.GrossAmount.Should().BeEqualTo(850m);
        await entry.StockLocationId.Should().BeEqualTo(1);

        // 4. Record settlement payout: Gross 850, Net 800 (Fee = 50 Tk)
        var today = AccountingCalendar.Today(null);
        var payoutInput = new RecordPayoutInput
        {
            StockLocationId = 1,
            Method = PaymentMethod.Card,
            ClearingEntryIds = [entry.Id],
            GrossAmount = 850m,
            NetAmount = 800m,
            BusinessDate = today,
            Reference = "BATCH-SETTLE-850",
            Notes = "Reconciled with bank statement"
        };

        var payoutId = await clearing.RecordPayoutAsync(AdminUser, payoutInput);
        await payoutId.Should().BeGreaterThan(0);

        // 5. Verify Clearing Entry is now Settled
        var settledEntry = await db.PaymentClearingEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        await settledEntry.Status.Should().BeEqualTo(PaymentClearingStatus.Settled);
        await settledEntry.PayoutId.Should().BeEqualTo(payoutId);

        // 6. Verify Bank Balance increased by exactly net +800
        var updatedBank = await db.BankAccounts.AsNoTracking().SingleAsync(b => b.Code == "MAIN");
        await updatedBank.CurrentBalance.Should().BeEqualTo(initialBankBalance + 800m);

        // 7. Verify Bank Movements:
        //    Must contain GROSS deposit (+850) and FEE deduction (-50)
        var bankMovements = await db.BankMovements.AsNoTracking()
            .Where(m => m.BusinessDate == today)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var grossMove = bankMovements.SingleOrDefault(m => m.Amount == 850m && m.Kind == CashMovementKind.Payout);
        await grossMove.Should().NotBeNull();
        await grossMove!.Notes.Should().Contain("850");

        var feeMove = bankMovements.SingleOrDefault(m => m.Amount == -50m && m.Kind == CashMovementKind.Expense);
        await feeMove.Should().NotBeNull();
        await feeMove!.ExpenseId.HasValue.Should().BeTrue();

        // 8. Verify Expense: 50 Tk attributed to Location 1 under Payment Processing Fees
        var expense = await db.Expenses.Include(e => e.CategoryDefinition).SingleOrDefaultAsync(e => e.Id == feeMove.ExpenseId!.Value);
        await expense.Should().NotBeNull();
        await expense!.Amount.Should().BeEqualTo(50m);
        await expense.StockLocationId.Should().BeEqualTo(1);
        await expense.CategoryDefinition!.Name.Should().BeEqualTo("Payment Processing Fees");
        await expense.Method.Should().BeEqualTo(PaymentMethod.BankTransfer);
    }
}

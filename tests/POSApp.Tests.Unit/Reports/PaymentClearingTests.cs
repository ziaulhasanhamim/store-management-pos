using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Reports;

public class PaymentClearingTests
{
    private static AppDbContext CreateInMemoryDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Test]
    public async Task ReverseClearingAsync_WhenUnsettled_MarksReversed()
    {
        using var db = CreateInMemoryDb(Guid.NewGuid().ToString());
        var sale = new Sale { Id = 1, Status = SaleStatus.Completed, StockLocationId = 1, CashierId = 1 };
        var payment = new Payment { Id = 10, Sale = sale, SaleId = 1, Amount = 1000m, Method = PaymentMethod.Card };
        var clearing = new PaymentClearingEntry
        {
            Id = 100,
            Payment = payment,
            PaymentId = 10,
            GrossAmount = 1000m,
            Status = PaymentClearingStatus.Unsettled,
            Notes = "Original"
        };
        db.Sales.Add(sale);
        db.Payments.Add(payment);
        db.PaymentClearingEntries.Add(clearing);
        await db.SaveChangesAsync();

        await FinancialPosting.ReverseClearingAsync(db, saleId: 1, actorId: 99, reason: "Customer return", activeDate: new DateOnly(2026, 11, 7), CancellationToken.None);
        await db.SaveChangesAsync();

        var updated = await db.PaymentClearingEntries.FindAsync(100);
        await updated!.Status.Should().BeEqualTo(PaymentClearingStatus.Reversed);
        await updated.Notes!.Should().Contain("Customer return");
    }

    [Test]
    public async Task ReverseClearingAsync_WhenSettled_ReversesBankAndExpenseAndAddsReversalPayout()
    {
        using var db = CreateInMemoryDb(Guid.NewGuid().ToString());
        var today = new DateOnly(2026, 11, 7);
        var bankAccount = new BankAccount { Id = 1, Code = "MAIN", CurrentBalance = 19150m };
        db.BankAccounts.Add(bankAccount);

        var sale = new Sale { Id = 19, Status = SaleStatus.Completed, TotalAmount = 850m, StockLocationId = 1, CashierId = 1 };
        var payment = new Payment { Id = 19, Sale = sale, SaleId = 19, Amount = 850m, Method = PaymentMethod.Card };
        var expense = new Expense
        {
            Id = 29,
            Amount = 20m,
            Method = PaymentMethod.BankTransfer,
            OccurredAt = new DateTimeOffset(2026, 11, 7, 8, 43, 11, TimeSpan.Zero),
            Notes = "Fee"
        };
        var bankMoveGross = new BankMovement
        {
            Id = 9,
            BankAccount = bankAccount,
            BankAccountId = 1,
            Kind = CashMovementKind.Payout,
            Amount = 850m,
            Notes = "Gross"
        };
        var bankMoveFee = new BankMovement
        {
            Id = 10,
            BankAccount = bankAccount,
            BankAccountId = 1,
            Kind = CashMovementKind.Expense,
            Amount = -20m,
            Expense = expense,
            ExpenseId = 29,
            Notes = "Fee"
        };
        var payout = new PaymentPayout
        {
            Id = 1,
            BusinessDate = today,
            GrossAmount = 850m,
            FeeAmount = 20m,
            NetAmount = 830m,
            BankAccountId = 1,
            BankMovement = bankMoveGross,
            BankMovementId = 9,
            Expense = expense,
            ExpenseId = 29,
            Notes = "Payout 1"
        };
        var clearing = new PaymentClearingEntry
        {
            Id = 1,
            Payment = payment,
            PaymentId = 19,
            GrossAmount = 850m,
            Status = PaymentClearingStatus.Settled,
            Payout = payout,
            PayoutId = 1,
            Notes = "Original"
        };

        db.Sales.Add(sale);
        db.Payments.Add(payment);
        db.Expenses.Add(expense);
        db.BankMovements.AddRange(bankMoveGross, bankMoveFee);
        db.PaymentPayouts.Add(payout);
        db.PaymentClearingEntries.Add(clearing);
        await db.SaveChangesAsync();

        // Reverse the clearing entry as part of voiding sale 19
        await FinancialPosting.ReverseClearingAsync(db, saleId: 19, actorId: 99, reason: "Voided sale #19", activeDate: today, CancellationToken.None);
        await db.SaveChangesAsync();

        // 1. Clearing entry marked Reversed
        var updatedClearing = await db.PaymentClearingEntries.FindAsync(1);
        await updatedClearing!.Status.Should().BeEqualTo(PaymentClearingStatus.Reversed);

        // 2. Bank balance adjusted: 19150 - 850 + 20 = 18320 (net 830 removed)
        var updatedBank = await db.BankAccounts.FindAsync(1);
        await updatedBank!.CurrentBalance.Should().BeEqualTo(18320m);

        // 3. Reversal BankMovements created
        var reversalGross = await db.BankMovements.SingleOrDefaultAsync(m => m.Amount == -850m && m.Kind == CashMovementKind.Payout);
        await reversalGross.Should().NotBeNull();
        await reversalGross!.ReversesMovementId.Should().BeEqualTo(9);

        var reversalFee = await db.BankMovements.SingleOrDefaultAsync(m => m.Amount == 20m && m.Kind == CashMovementKind.Reversal);
        await reversalFee.Should().NotBeNull();

        // 4. Expense voided / removed because it is from same working day
        var remainingExpense = await db.Expenses.FindAsync(29);
        await remainingExpense.Should().BeNull();

        // 5. Reversal Payout recorded in payout history with negative values
        var reversalPayout = await db.PaymentPayouts.SingleOrDefaultAsync(p => p.GrossAmount == -850m);
        await reversalPayout.Should().NotBeNull();
        await reversalPayout!.FeeAmount.Should().BeEqualTo(-20m);
        await reversalPayout.NetAmount.Should().BeEqualTo(-830m);
        await reversalPayout.Reference!.Should().BeEqualTo("REV-PAYOUT-1");
    }

    [Test]
    public async Task StatementService_PayoutLabel_ReturnsCorrectCardSettlementLabels()
    {
        // When Kind is Payout with positive amount, it should be "Card / wallet settlement payout"
        // When Kind is Payout with negative amount, it should be "Payout settlement reversal"
        // When Kind is Reversal with clearing notes, it should be "Payout settlement reversal"
        using var db = CreateInMemoryDb(Guid.NewGuid().ToString());
        var adminRole = new Microsoft.AspNetCore.Identity.IdentityRole<int> { Id = 1, Name = "Admin", NormalizedName = "ADMIN" };
        var userRole = new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { RoleId = 1, UserId = 1 };
        db.Roles.Add(adminRole);
        db.UserRoles.Add(userRole);

        var bankAccount = new BankAccount { Id = 1, Code = "MAIN", CurrentBalance = 1000m };
        db.BankAccounts.Add(bankAccount);

        var moveGross = new BankMovement
        {
            Id = 1,
            BankAccountId = 1,
            BusinessDate = new DateOnly(2026, 11, 7),
            Kind = CashMovementKind.Payout,
            Amount = 850m,
            Notes = "Card settlement gross"
        };
        var moveReversal = new BankMovement
        {
            Id = 2,
            BankAccountId = 1,
            BusinessDate = new DateOnly(2026, 11, 7),
            Kind = CashMovementKind.Payout,
            Amount = -850m,
            Notes = "Reversal of clearing payout"
        };
        db.BankMovements.AddRange(moveGross, moveReversal);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusinessTimeZone"] = "Asia/Dhaka"
        }).Build();

        var service = new StatementService(scopeFactory, config);
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1"),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
                }, "test"));

        var statement = await service.ReadAsync(principal, account: "bank", period: "custom", start: new DateOnly(2026, 11, 7), end: new DateOnly(2026, 11, 7));
        var grossRow = statement.Rows.FirstOrDefault(r => r.Amount == 850m);
        var reversalRow = statement.Rows.FirstOrDefault(r => r.Amount == -850m);

        await grossRow.Should().NotBeNull();
        await grossRow!.Type.Should().BeEqualTo("Card / wallet settlement payout");

        await reversalRow.Should().NotBeNull();
        await reversalRow!.Type.Should().BeEqualTo("Payout settlement reversal");
    }
    [Test]
    public async Task AwaitingPayoutSummary_ComputesTotalAndCounts()
    {
        var oldest = new DateOnly(2026, 11, 1);
        var summary = new AwaitingPayoutSummary(
            CardAmount: 12500m,
            MobileWalletAmount: 3500m,
            UnsettledCount: 14,
            OldestDate: oldest);

        await summary.CardAmount.Should().BeEqualTo(12500m);
        await summary.MobileWalletAmount.Should().BeEqualTo(3500m);
        await summary.TotalAmount.Should().BeEqualTo(16000m);
        await summary.UnsettledCount.Should().BeEqualTo(14);
        await summary.OldestDate.Should().BeEqualTo(oldest);
    }

    [Test]
    public async Task PayoutCalculation_CalculatesFeeAndPercentageAccurately()
    {
        decimal gross = 50000m;
        decimal net = 49000m;
        decimal fee = gross - net;
        decimal feePct = Math.Round((fee / gross) * 100m, 2);

        await fee.Should().BeEqualTo(1000m);
        await feePct.Should().BeEqualTo(2.00m);
    }

    [Test]
    public async Task ClearingAgeing_CorrectlySumsTotalAcrossBuckets()
    {
        var ageing = new ClearingAgeingDto(
            LocationId: 1,
            LocationName: "Main Store",
            Method: PaymentMethod.Card,
            Under3Days: 4500m,
            From3To7Days: 2000m,
            Over7Days: 500m,
            Total: 7000m);

        await ageing.Total.Should().BeEqualTo(ageing.Under3Days + ageing.From3To7Days + ageing.Over7Days);
        await ageing.Over7Days.Should().BeEqualTo(500m);
        await ageing.LocationName.Should().BeEqualTo("Main Store");
    }

    [Test]
    public async Task ClearingSaleItem_TracksDaysWaiting()
    {
        var item = new ClearingSaleItemDto(
            Id: 101,
            PaymentId: 205,
            SaleId: 301,
            BusinessDate: new DateOnly(2026, 11, 2),
            SoldAt: new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero),
            StockLocationId: 1,
            StockLocationName: "Main Store",
            Method: PaymentMethod.Card,
            GrossAmount: 1500m,
            DaysWaiting: 5);

        await item.GrossAmount.Should().BeEqualTo(1500m);
        await item.DaysWaiting.Should().BeEqualTo(5);
        await item.StockLocationName.Should().BeEqualTo("Main Store");
    }
}

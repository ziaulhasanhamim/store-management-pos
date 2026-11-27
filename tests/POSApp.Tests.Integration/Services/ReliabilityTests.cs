using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Security;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class ReliabilityTests : PosIntegrationTestBase
{
    private static SalesCommand Sale(Guid? id = null) => new(id ?? Guid.NewGuid(), 1, 1,
        [new SalesLine(1, 1m, 150m)], 0m, 150m, PaymentMethod.Cash, "Reliability regression");

    [Test]
    public async Task Checkout_ConcurrentIdenticalRequests_PostStockAndCashOnce()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var command = Sale();
        var results = await Task.WhenAll(sales.SaveAsync(SalesUser, command, true), sales.SaveAsync(SalesUser, command, true));
        await Assert.That(results[0].Id).IsEqualTo(results[1].Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.That(await db.Sales.CountAsync()).IsEqualTo(1);
        await Assert.That(await db.Payments.CountAsync()).IsEqualTo(1);
        await Assert.That(await db.InventoryMovements.CountAsync(m => m.Type == InventoryMovementType.Sale)).IsEqualTo(1);
        await Assert.That((await db.InventoryBalances.SingleAsync(b => b.ProductId == 1 && b.StockLocationId == 1)).QuantityOnHand).IsEqualTo(-1m);
        await Assert.That((await db.CashDrawers.SingleAsync(d => d.Id == 1)).CurrentCash).IsEqualTo(100150m);
    }

    [Test]
    public async Task Checkout_ConcurrentDistinctSales_PreservesDocumentedNegativeStockAndCash()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        await Task.WhenAll(sales.SaveAsync(SalesUser, Sale(), true), sales.SaveAsync(SalesUser, Sale(), true));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.That(await db.Sales.CountAsync()).IsEqualTo(2);
        await Assert.That((await db.InventoryBalances.SingleAsync(b => b.ProductId == 1 && b.StockLocationId == 1)).QuantityOnHand).IsEqualTo(-2m);
        await Assert.That(await db.Payments.SumAsync(p => p.Amount)).IsEqualTo(300m);
        await Assert.That((await db.CashDrawers.SingleAsync(d => d.Id == 1)).CurrentCash).IsEqualTo(100300m);
    }

    [Test]
    public async Task Checkout_RetryAfterPreviousDuePayment_ReturnsOriginalResultWithoutDoublePosting()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CustomerBalances.Add(new CustomerBalance { CustomerId = 1, OpeningBalance = 500m, CurrentBalance = 500m, OpenedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var command = Sale() with { Payment = 650m };
        var first = await sales.SaveAsync(SalesUser, command, true);
        var retry = await sales.SaveAsync(SalesUser, command, true);
        await Assert.That(retry).IsEqualTo(first);
        await Assert.That(retry.Due).IsEqualTo(0m);
        await Assert.That(await db.Payments.CountAsync()).IsEqualTo(1);
        await Assert.That((await db.CustomerBalances.AsNoTracking().SingleAsync()).CurrentBalance).IsEqualTo(0m);
    }

    [Test]
    public async Task Checkout_ChangedTimestampOnCompletedRequest_IsRejected()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var command = Sale() with { TransactionTime = DateTimeOffset.UtcNow.AddDays(-2) };
        await sales.SaveAsync(AdminUser, command, true);
        await Assert.ThrowsAsync<ValidationException>(() => sales.SaveAsync(AdminUser, command with { TransactionTime = command.TransactionTime.Value.AddDays(1) }, true));
    }

    [Test]
    public async Task BackdatedSale_ClosedEffectiveDay_IsRejectedWithoutPosting()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var at = AccountingCalendar.At(AccountingCalendar.Today(null).AddDays(-2), null);
        db.NonTradingDays.Add(new NonTradingDay { MarkedById = 1, Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, AccountingCalendar.Zone(null)).DateTime), Reason = "Closed" });
        await db.SaveChangesAsync();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        await Assert.ThrowsAsync<ValidationException>(() => sales.SaveAsync(AdminUser, Sale() with { TransactionTime = at }, true));
        await Assert.That(await db.Sales.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.Payments.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task BackdatedReceiving_ClosedEffectiveDay_IsRejectedWithoutPosting()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var date = AccountingCalendar.Today(null).AddDays(-2);
        db.NonTradingDays.Add(new NonTradingDay { MarkedById = 1, Date = date, Reason = "Closed" });
        await db.SaveChangesAsync();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var command = new ReceiveCommand(Guid.NewGuid(), 1, 1, [new ReceiveLine(1, 1m, 100m)], 0m, PaymentMethod.Cash, null, "Regression") { TransactionTime = AccountingCalendar.At(date, null) };
        await Assert.ThrowsAsync<ValidationException>(() => inventory.ReceiveAsync(AdminUser, command));
        await Assert.That(await db.Purchases.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.InventoryMovements.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task BackdatedSale_NonAdmin_ThrowsUnauthorizedAccessException()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var command = Sale() with { TransactionTime = DateTimeOffset.UtcNow.AddDays(-1) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sales.SaveAsync(SalesUser, command, true));
    }

    [Test]
    public async Task BackdatedReceiving_NonAdmin_ThrowsUnauthorizedAccessException()
    {
        await using var scope = CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var command = new ReceiveCommand(Guid.NewGuid(), 1, 1, [new ReceiveLine(1, 1m, 100m)], 0m, PaymentMethod.Cash, null, "Regression")
        {
            TransactionTime = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.ReceiveAsync(InventoryUser, command));
    }

    [Test]
    public async Task Authorization_RevokedAdminRole_DeniesStaleClaimsInServiceAndHandler()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            UserName = "tempadmin",
            NormalizedUserName = "TEMPADMIN",
            CashLocationId = 1,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { UserId = user.Id, RoleId = 1 });
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(user.Id, "tempadmin", POSApp.Client.Security.AppRoles.Admin);
        db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == user.Id).ToListAsync());
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => StoreAccess.RequireAsync(db, principal, POSApp.Client.Security.AppRoles.Admin));
        await Assert.That(await StoreAccess.HasPolicyAsync(db, principal, POSApp.Client.Security.AppRoles.Admin)).IsFalse();
        var requirement = new DynamicPolicyRequirement(AppPolicies.ManageUsers);
        var context = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext([requirement], principal, null);
        var handler = new DynamicPolicyAuthorizationHandler(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());
        await handler.HandleAsync(context);
        await Assert.That(context.HasSucceeded).IsFalse();
    }

    [Test]
    public async Task BackdatedExpense_ClosedEffectiveDay_IsRejectedWithoutPosting()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var date = AccountingCalendar.Today(null).AddDays(-2);
        db.NonTradingDays.Add(new NonTradingDay { MarkedById = 1, Date = date, Reason = "Closed" });
        var category = new ExpenseCategoryDefinition { Name = "Regression category", NormalizedName = "REGRESSION CATEGORY" };
        db.ExpenseCategories.Add(category);
        await db.SaveChangesAsync();
        var entries = scope.ServiceProvider.GetRequiredService<MoneyEntryService>();
        var command = new MoneyEntryCommand(Guid.NewGuid(), AccountingCalendar.Today(null),
            [new MoneyEntryInput { CategoryId = category.Id, Amount = 100m, StockLocationId = 1, TransactionTime = AccountingCalendar.At(date, null), Notes = "Regression expense" }]);
        await Assert.ThrowsAsync<ValidationException>(() => entries.SaveAsync(AdminUser, "expenses", command));
        await Assert.That(await db.Expenses.CountAsync()).IsEqualTo(0);
        await Assert.That((await db.CashDrawers.AsNoTracking().SingleAsync(d => d.Id == 1)).CurrentCash).IsEqualTo(100000m);
    }

    [Test]
    public async Task Checkout_SplitPaymentAndRefund_RestoreStockCashAndCustomerLedger()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var sale = Sale() with { Payment = 0m, Payments = [new PaymentInput(PaymentMethod.Cash, 50m), new PaymentInput(PaymentMethod.Card, 100m)] };
        var original = await sales.SaveAsync(SalesUser, sale, true);
        var refund = Sale() with { Lines = [new SalesLine(1, -1m, 150m)], Payment = 0m, OriginalSaleId = original.Id,
            Payments = [new PaymentInput(PaymentMethod.Cash, -50m), new PaymentInput(PaymentMethod.Card, -100m)] };
        var result = await sales.SaveAsync(SalesUser, refund, true);
        await Assert.That(result.Total).IsEqualTo(-150m);
        await Assert.That(result.Paid).IsEqualTo(-150m);
        await Assert.That(result.Due).IsEqualTo(0m);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.That((await db.InventoryBalances.SingleAsync(b => b.ProductId == 1 && b.StockLocationId == 1)).QuantityOnHand).IsEqualTo(0m);
        await Assert.That((await db.CashDrawers.SingleAsync(d => d.Id == 1)).CurrentCash).IsEqualTo(100000m);
        await Assert.That((await db.CustomerBalances.SingleAsync(b => b.CustomerId == 1)).CurrentBalance).IsEqualTo(0m);
        await Assert.That(await db.Payments.SumAsync(p => p.Amount)).IsEqualTo(0m);
        await Assert.That(await db.PaymentClearingEntries.SumAsync(p => p.GrossAmount)).IsEqualTo(0m);
    }

    [Test]
    public async Task Startup_DefaultDoesNotRepairHistoricalMovementTimestamps()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var originalAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var movement = new CashMovement { CashDrawerId = 1, RequestId = Guid.NewGuid(), Kind = CashMovementKind.CashIn, Notes = "Historical regression",
            OccurredAt = originalAt, Amount = 1m, CreatedByUserId = 1 };
        db.CashMovements.Add(movement);
        await db.SaveChangesAsync();
        await using var factory = new PosWebApplicationFactory();
        using var client = factory.CreateAdminClient();
        await Assert.That((await client.GetAsync("/health")).IsSuccessStatusCode).IsTrue();
        await Assert.That((await db.CashMovements.AsNoTracking().SingleAsync(m => m.Id == movement.Id)).OccurredAt).IsEqualTo(originalAt);
    }

    [Test]
    public async Task Authorization_DemotedAdminCannotReadAnotherCashiersBill()
    {
        await using var scope = CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
        var sale = await sales.SaveAsync(SalesUser, Sale(), true);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == 1).ToListAsync());
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { UserId = 1, RoleId = 3 });
        await db.SaveChangesAsync();
        await Assert.That(await sales.BillAsync(AdminUser, sale.Id)).IsNull();
        var clearing = scope.ServiceProvider.GetRequiredService<PaymentClearingService>();
        await Assert.That((await clearing.GetPageDataAsync(AdminUser)).CanManagePayouts).IsFalse();
    }
}

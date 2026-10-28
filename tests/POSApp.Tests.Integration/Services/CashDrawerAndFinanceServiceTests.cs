using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class CashDrawerAndFinanceServiceTests : PosIntegrationTestBase
{
    [Test]
    public async Task CashDrawer_OpenAdjustAndClose_MaintainsAccurateSessionAndMovements()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create a new location to test drawer full lifecycle from scratch
        var locId = await inventory.AddLocationAsync(AdminUser, "North Outlet", canSell: true);

        // 1. Open drawer with 500 opening cash
        var openInput = new OpenCashInput
        {
            RequestId = Guid.NewGuid(),
            StockLocationId = locId,
            OpeningCash = 500m
        };
        var sessionId = await drawerService.OpenAsync(AdminUser, openInput);
        await sessionId.Should().BeGreaterThan(0);

        var sessionInDb = await db.CashSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId);
        await sessionInDb.Should().NotBeNull();
        await sessionInDb!.OpeningCash.Should().BeEqualTo(500m);
        await sessionInDb.ExpectedCash.Should().BeEqualTo(500m);

        // 2. Adjust cash: add 200 petty cash deposit
        var adjustInput = new CashAdjustmentInput
        {
            RequestId = Guid.NewGuid(),
            SessionId = sessionId,
            Payout = false,
            Amount = 200m,
            Notes = "Midday cash addition"
        };
        await drawerService.AdjustAsync(AdminUser, adjustInput);

        // Check via ReadAsync
        var page = await drawerService.ReadAsync(AdminUser, sessionId);
        await page.Current.Should().NotBeNull();
        await page.Current!.Expected.Should().BeEqualTo(700m);

        // 3. Close drawer with actual cash 700
        var closeInput = new CloseCashInput
        {
            SessionId = sessionId,
            ExpectedCash = 700m,
            ActualCash = 700m,
            Confirmed = true,
            Notes = "Clean close"
        };
        await drawerService.CloseAsync(AdminUser, closeInput);

        var closedSession = await db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        await closedSession.ClosedAt.HasValue.Should().BeTrue();
        await closedSession.ActualClosingCash.Should().BeEqualTo(700m);
        await closedSession.Difference.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task ClosingService_DailyClosing_ComputesAccurateRevenueFromRealSales()
    {
        var today = AccountingCalendar.Today(null);

        // 1. Setup product & stock
        int productId;
        await using (var scope = CreateScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
            productId = await inventory.SaveProductAsync(InventoryUser, null, new ProductInput
            {
                Name = "Wireless Bluetooth Earbuds",
                Unit = "piece",
                CostPrice = 30m,
                SellingPrice = 75m,
                SupplierId = 1,
                CategoryId = 1
            });

            await inventory.ReceiveAsync(InventoryUser, new ReceiveCommand(
                RequestId: Guid.NewGuid(),
                SupplierId: 1,
                LocationId: 1,
                Lines: [new ReceiveLine(productId, Quantity: 10m, UnitCost: 30m)],
                Payment: 300m,
                Method: PaymentMethod.Cash,
                Invoice: "PO-EARBUDS-001",
                Notes: "Initial batch"));
        }

        // 2. Complete sale for 2 units @ 75 = 150
        await using (var scope = CreateScope())
        {
            var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
            await sales.SaveAsync(SalesUser, new SalesCommand(
                RequestId: Guid.NewGuid(),
                LocationId: 1,
                CustomerId: 1,
                Lines: [new SalesLine(productId, Quantity: 2m, UnitPrice: 75m)],
                Discount: 0m,
                Payment: 150m,
                Method: PaymentMethod.Cash,
                Notes: "Closing calculation test sale"), complete: true);
        }

        // 3. Read daily closing totals
        await using (var scope = CreateScope())
        {
            var closingService = scope.ServiceProvider.GetRequiredService<ClosingService>();
            var totals = await closingService.ReadAsync(AdminUser, today, ClosingKind.Day);

            await totals.Should().NotBeNull();
            await totals.Revenue.Should().BeEqualTo(150m);
            await totals.SalesReceipts.Should().BeEqualTo(150m);
            await totals.Cogs.Should().BeEqualTo(60m); // 2 units @ 30 cost
        }
    }

    [Test]
    public async Task BalanceIntegrityService_WhenConsistent_ReportsZeroIssues()
    {
        await using var scope = CreateScope();
        var integrity = scope.ServiceProvider.GetRequiredService<BalanceIntegrityService>();

        var issues = await integrity.ReadAsync(AdminUser);
        await issues.Should().NotBeNull();
        await issues.Count.Should().BeEqualTo(0);
    }
}

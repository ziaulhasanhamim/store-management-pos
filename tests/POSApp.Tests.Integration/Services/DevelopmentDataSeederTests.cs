using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class DevelopmentDataSeederTests : PosIntegrationTestBase
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "POSApp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentSeed:Enabled"] = "true",
                ["BusinessTimeZone"] = "Asia/Dhaka"
            })
            .Build();

    [Test]
    public async Task SeedAsync_ValidData_SeedsAllEntitiesAndDrawerCorrectly()
    {
        // Clear CashLocationId on admin user to test that seeder assigns it to the primary selling location
        await using (var setupScope = CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminToUnassign = await setupDb.Users.SingleAsync(u => u.Id == 1);
            adminToUnassign.CashLocationId = null;
            await setupDb.SaveChangesAsync();
        }

        var env = new TestHostEnvironment();
        var config = CreateConfig();

        await DevelopmentDataSeeder.SeedAsync(Services, env, config);

        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Locations
        var mainLoc = await db.StockLocations.SingleOrDefaultAsync(l => l.Code == "DEMO-MAIN");
        await mainLoc.Should().NotBeNull();
        await mainLoc!.CanSell.Should().BeTrue();
        await string.IsNullOrWhiteSpace(mainLoc.Address).Should().BeFalse();
        await string.IsNullOrWhiteSpace(mainLoc.Phone).Should().BeFalse();

        var backLoc = await db.StockLocations.SingleOrDefaultAsync(l => l.Code == "DEMO-BACK");
        await backLoc.Should().NotBeNull();
        await backLoc!.CanSell.Should().BeFalse();
        await string.IsNullOrWhiteSpace(backLoc.Address).Should().BeFalse();

        // Categories
        var electronics = await db.Categories.SingleOrDefaultAsync(c => c.Name == "Electronics");
        await electronics.Should().NotBeNull();
        await string.IsNullOrWhiteSpace(electronics!.Description).Should().BeFalse();

        // Suppliers
        var aster = await db.Suppliers.Include(s => s.Balance).SingleOrDefaultAsync(s => s.Name == "Aster Wholesale (Demo)");
        await aster.Should().NotBeNull();
        await string.IsNullOrWhiteSpace(aster!.Email).Should().BeFalse();
        await aster.Balance.Should().NotBeNull();
        await aster.Balance!.CurrentBalance.Should().BeEqualTo(2400m);

        // Customers
        var mira = await db.Customers.Include(c => c.Balance).SingleOrDefaultAsync(c => c.Name == "Mira Rahman (Demo)");
        await mira.Should().NotBeNull();
        await string.IsNullOrWhiteSpace(mira!.Phone).Should().BeFalse();
        await mira.Balance.Should().NotBeNull();
        await mira.Balance!.CurrentBalance.Should().BeEqualTo(750m);

        // Products
        var mouse = await db.Products.Include(p => p.InventoryBalances).SingleOrDefaultAsync(p => p.Barcode == "DEMO-1001");
        await mouse.Should().NotBeNull();
        await mouse!.CostPrice.Should().BeEqualTo(550m);
        await mouse.SellingPrice.Should().BeEqualTo(790m);
        await string.IsNullOrWhiteSpace(mouse.Description).Should().BeFalse();
        var mouseStock = mouse.InventoryBalances.Sum(b => b.QuantityOnHand);
        await mouseStock.Should().BeEqualTo(36m); // 24 main + 12 back

        // Non-stocked product
        var service = await db.Products.SingleOrDefaultAsync(p => p.Barcode == "DEMO-1007");
        await service.Should().NotBeNull();
        await service!.IsNonStocked.Should().BeTrue();
        await service.CostPrice.Should().BeEqualTo(0m);
        await service.SellingPrice.Should().BeEqualTo(300m);

        // Fractional product
        var tape = await db.Products.Include(p => p.InventoryBalances).SingleOrDefaultAsync(p => p.Barcode == "DEMO-1008");
        await tape.Should().NotBeNull();
        await tape!.AllowFractionUnit.Should().BeTrue();
        var tapeStock = tape.InventoryBalances.Sum(b => b.QuantityOnHand);
        await tapeStock.Should().BeEqualTo(50.5m);

        // ProductCostHistory seeded
        var costHistory = await db.ProductCostHistories.Where(h => h.ProductId == mouse.Id).ToListAsync();
        await costHistory.Count.Should().BeGreaterThan(0);
        await costHistory[0].NewCostPrice.Should().BeEqualTo(550m);

        // CashDrawer & Session
        var drawer = await db.CashDrawers.Include(d => d.CurrentSession)
            .SingleOrDefaultAsync(d => d.StockLocationId == mainLoc.Id);
        await drawer.Should().NotBeNull();
        await drawer!.OpeningBalance.Should().BeEqualTo(10000m);
        await drawer.CurrentCash.Should().BeEqualTo(10000m);
        await drawer.CurrentSession.Should().NotBeNull();
        await drawer.CurrentSession!.ClosedAt.Should().BeNull();
        await drawer.CurrentSession.OpeningCash.Should().BeEqualTo(10000m);

        // DayFinance
        var dayFinance = await db.DayFinances.SingleOrDefaultAsync(df => df.Date == drawer.CurrentSession.BusinessDate);
        await dayFinance.Should().NotBeNull();
        await dayFinance!.OpeningCash.Should().BeEqualTo(10000m);

        // BankAccount
        var bank = await db.BankAccounts.SingleOrDefaultAsync(b => b.Code == "MAIN");
        await bank.Should().NotBeNull();
        await bank!.OpeningBalance.Should().BeEqualTo(50000m);
        await bank.CurrentBalance.Should().BeEqualTo(50000m);

        // User cash location assigned
        var admin = await db.Users.SingleOrDefaultAsync(u => u.Id == 1);
        await admin.Should().NotBeNull();
        await admin!.CashLocationId.Should().BeEqualTo(mainLoc.Id);
    }

    [Test]
    public async Task SeedAsync_Idempotent_SecondRunDoesNotDuplicateOrCorrupt()
    {
        var env = new TestHostEnvironment();
        var config = CreateConfig();

        // First run
        await DevelopmentDataSeeder.SeedAsync(Services, env, config);

        await using var scope1 = CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();

        var productCount1 = await db1.Products.CountAsync();
        var supplierCount1 = await db1.Suppliers.CountAsync();
        var customerCount1 = await db1.Customers.CountAsync();
        var categoryCount1 = await db1.Categories.CountAsync();
        var locationCount1 = await db1.StockLocations.CountAsync();
        var drawerCount1 = await db1.CashDrawers.CountAsync();
        var sessionCount1 = await db1.CashSessions.CountAsync();
        var movementCount1 = await db1.InventoryMovements.CountAsync();

        // Second run
        await DevelopmentDataSeeder.SeedAsync(Services, env, config);

        await using var scope2 = CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var productCount2 = await db2.Products.CountAsync();
        var supplierCount2 = await db2.Suppliers.CountAsync();
        var customerCount2 = await db2.Customers.CountAsync();
        var categoryCount2 = await db2.Categories.CountAsync();
        var locationCount2 = await db2.StockLocations.CountAsync();
        var drawerCount2 = await db2.CashDrawers.CountAsync();
        var sessionCount2 = await db2.CashSessions.CountAsync();
        var movementCount2 = await db2.InventoryMovements.CountAsync();

        await productCount2.Should().BeEqualTo(productCount1);
        await supplierCount2.Should().BeEqualTo(supplierCount1);
        await customerCount2.Should().BeEqualTo(customerCount1);
        await categoryCount2.Should().BeEqualTo(categoryCount1);
        await locationCount2.Should().BeEqualTo(locationCount1);
        await drawerCount2.Should().BeEqualTo(drawerCount1);
        await sessionCount2.Should().BeEqualTo(sessionCount1);
        await movementCount2.Should().BeEqualTo(movementCount1);
    }

    [Test]
    public async Task SeedAsync_AllowsImmediateCashSaleWithoutErrors()
    {
        var env = new TestHostEnvironment();
        var config = CreateConfig();

        await DevelopmentDataSeeder.SeedAsync(Services, env, config);

        await using var scope = CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<SalesService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mainLoc = await db.StockLocations.SingleAsync(l => l.Code == "DEMO-MAIN");
        var mouse = await db.Products.SingleAsync(p => p.Barcode == "DEMO-1001");
        var customer = await db.Customers.SingleAsync(c => c.Name == "Walk-in Account (Demo)");

        var saleCommand = new SalesCommand(
            RequestId: Guid.NewGuid(),
            LocationId: mainLoc.Id,
            CustomerId: customer.Id,
            Lines: [new SalesLine(mouse.Id, Quantity: 2, UnitPrice: mouse.SellingPrice)],
            Discount: 0m,
            Payment: 2 * mouse.SellingPrice,
            Method: PaymentMethod.Cash,
            Notes: "Development seed cash sale verification");

        var saleResult = await salesService.SaveAsync(AdminUser, saleCommand, complete: true);
        await saleResult.Should().NotBeNull();
        await saleResult.Id.Should().BeGreaterThan(0);

        // Verify drawer expected cash updated after cash sale
        var drawer = await db.CashDrawers.Include(d => d.CurrentSession)
            .SingleAsync(d => d.StockLocationId == mainLoc.Id);
        await drawer.CurrentCash.Should().BeEqualTo(10000m + (2 * mouse.SellingPrice));
    }
}

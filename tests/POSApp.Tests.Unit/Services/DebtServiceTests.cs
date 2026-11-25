using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Security;
using POSApp.Services;

namespace POSApp.Tests.Unit.Services;

[NotInParallel]
public class DebtServiceTests
{
    private static (IServiceScopeFactory Scopes, IConfiguration Config, AppDbContext Db) CreateTestEnvironment(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var db = provider.GetRequiredService<AppDbContext>();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusinessTimeZone"] = "Asia/Dhaka",
            ["StoreName"] = "JAS International"
        }).Build();

        return (scopeFactory, config, db);
    }

    private static ClaimsPrincipal CreateAdminPrincipal() =>
        new(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, "superuser"),
            new(ClaimTypes.Role, AppRoles.Admin)
        ], "test"));

    [Test]
    public async Task ReadAsync_EmptyDatabase_ExecutesWithoutException()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        
        // Setup admin user & role in db
        db.Roles.Add(new() { Id = 1, Name = AppRoles.Admin, NormalizedName = "ADMIN" });
        db.Users.Add(new() { Id = 1, UserName = "superuser", NormalizedUserName = "SUPERUSER" });
        db.UserRoles.Add(new() { RoleId = 1, UserId = 1 });
        await db.SaveChangesAsync();

        var service = new DebtService(scopes, config);
        var page = await service.ReadAsync(CreateAdminPrincipal(), historyPage: 1);

        await page.Should().NotBeNull();
        await page.History.Should().BeEmpty();
        await page.Lenders.Should().BeEmpty();
    }

    [Test]
    public async Task ReadAsync_WithLenderAndDebtEntries_ExecutesWithoutException()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        
        db.Roles.Add(new() { Id = 1, Name = AppRoles.Admin, NormalizedName = "ADMIN" });
        db.Users.Add(new() { Id = 1, UserName = "superuser", NormalizedUserName = "SUPERUSER" });
        db.UserRoles.Add(new() { RoleId = 1, UserId = 1 });
        await db.SaveChangesAsync();

        var service = new DebtService(scopes, config);
        var admin = CreateAdminPrincipal();

        var lenderId = await service.AddLenderAsync(admin, new LenderInput
        {
            Name = "Bank Asia Loan",
            Phone = "01700000000",
            OpeningBalance = 50000m,
            WorkingDate = AccountingCalendar.Today(config)
        });

        var page = await service.ReadAsync(admin, historyPage: 1);
        await page.Should().NotBeNull();
        await page.Lenders.Count.Should().BeEqualTo(1);
        await page.History.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ReadAsync_LenderWithoutBalance_ThrowsOrHandles()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        
        db.Roles.Add(new() { Id = 1, Name = AppRoles.Admin, NormalizedName = "ADMIN" });
        db.Users.Add(new() { Id = 1, UserName = "superuser", NormalizedUserName = "SUPERUSER" });
        db.UserRoles.Add(new() { RoleId = 1, UserId = 1 });
        
        // Add a lender with NO balance
        db.Lenders.Add(new() { Id = 1, Name = "Orphan Lender", RequestId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var service = new DebtService(scopes, config);
        var page = await service.ReadAsync(CreateAdminPrincipal(), historyPage: 1);
        await page.Should().NotBeNull();
        await page.Lenders.Count.Should().BeEqualTo(1);
        await page.Lenders[0].Opening.Should().BeEqualTo(0m);
        await page.Lenders[0].Balance.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task ReadAsync_DebtEntryWithMissingOrphanBalance_ExecutesGracefully()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        
        db.Roles.Add(new() { Id = 1, Name = AppRoles.Admin, NormalizedName = "ADMIN" });
        db.Users.Add(new() { Id = 1, UserName = "superuser", NormalizedUserName = "SUPERUSER" });
        db.UserRoles.Add(new() { RoleId = 1, UserId = 1 });

        // Add a lender and balance
        var lender = new Lender { Id = 100, Name = "Apex Lender", RequestId = Guid.NewGuid() };
        db.Lenders.Add(lender);
        var balance = new LenderBalance
        {
            Id = 10,
            LenderId = 100,
            OpeningDate = new DateOnly(2026, 11, 24)
        };
        db.LenderBalances.Add(balance);

        // Add a debt entry referencing this balance
        db.DebtEntries.Add(new()
        {
            RequestId = Guid.NewGuid(),
            LenderBalanceId = 10,
            BusinessDate = new DateOnly(2026, 11, 24),
            Kind = DebtEntryKind.Borrowing,
            Amount = 1000m,
            BalanceAfter = 1000m,
            Notes = "Direct loan",
            CreatedByUserId = 1
        });
        await db.SaveChangesAsync();

        var service = new DebtService(scopes, config);
        var page = await service.ReadAsync(CreateAdminPrincipal(), historyPage: 1);

        await page.Should().NotBeNull();
        await page.History.Count.Should().BeEqualTo(1);
        await page.History[0].Lender.Should().BeEqualTo("Apex Lender");
    }

    [Test]
    public async Task ReadAsync_WithDayEntries_CalculatesDayTotalsAndEntriesCorrectly()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        
        db.Roles.Add(new() { Id = 1, Name = AppRoles.Admin, NormalizedName = "ADMIN" });
        db.Users.Add(new() { Id = 1, UserName = "superuser", NormalizedUserName = "SUPERUSER" });
        db.UserRoles.Add(new() { RoleId = 1, UserId = 1 });

        var lender = new Lender { Id = 1, Name = "Premier Lender", RequestId = Guid.NewGuid() };
        db.Lenders.Add(lender);
        var balance = new LenderBalance
        {
            Id = 1,
            LenderId = 1,
            OpeningDate = new DateOnly(2026, 11, 24),
            CurrentBalance = 16000m
        };
        db.LenderBalances.Add(balance);

        var today = AccountingCalendar.Today(config);
        var yesterday = today.AddDays(-1);

        // 1. Borrowing today (10,000)
        db.DebtEntries.Add(new()
        {
            RequestId = Guid.NewGuid(),
            LenderBalanceId = 1,
            BusinessDate = today,
            Kind = DebtEntryKind.Borrowing,
            Amount = 10000m,
            BalanceAfter = 10000m,
            Notes = "Borrowed cash",
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedByUserId = 1
        });

        // 2. Repayment today (-4,000)
        db.DebtEntries.Add(new()
        {
            RequestId = Guid.NewGuid(),
            LenderBalanceId = 1,
            BusinessDate = today,
            Kind = DebtEntryKind.Repayment,
            Amount = -4000m,
            BalanceAfter = 6000m,
            Notes = "Repaid partial",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedByUserId = 1
        });

        // 3. Borrowing yesterday (5,000)
        db.DebtEntries.Add(new()
        {
            RequestId = Guid.NewGuid(),
            LenderBalanceId = 1,
            BusinessDate = yesterday,
            Kind = DebtEntryKind.Borrowing,
            Amount = 5000m,
            BalanceAfter = 5000m,
            Notes = "Borrowed yesterday",
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = 1
        });

        await db.SaveChangesAsync();

        var service = new DebtService(scopes, config);
        var admin = CreateAdminPrincipal();

        // Query today (default)
        var todayPage = await service.ReadAsync(admin);
        await todayPage.Should().NotBeNull();
        await todayPage.ViewDate.Should().BeEqualTo(today);
        await todayPage.DayCount.Should().BeEqualTo(2);
        await todayPage.DayEntries.Count.Should().BeEqualTo(2);
        await todayPage.DayBorrowed.Should().BeEqualTo(10000m);
        await todayPage.DayRepaid.Should().BeEqualTo(4000m);
        await todayPage.DayNet.Should().BeEqualTo(6000m);
        await todayPage.BorrowedToday.Should().BeEqualTo(10000m);
        await todayPage.RepaidToday.Should().BeEqualTo(4000m);

        // Query yesterday
        var yesterdayPage = await service.ReadAsync(admin, date: yesterday);
        await yesterdayPage.Should().NotBeNull();
        await yesterdayPage.ViewDate.Should().BeEqualTo(yesterday);
        await yesterdayPage.DayCount.Should().BeEqualTo(1);
        await yesterdayPage.DayEntries.Count.Should().BeEqualTo(1);
        await yesterdayPage.DayBorrowed.Should().BeEqualTo(5000m);
        await yesterdayPage.DayRepaid.Should().BeEqualTo(0m);
        await yesterdayPage.DayNet.Should().BeEqualTo(5000m);
        // Working day stats remain today's
        await yesterdayPage.BorrowedToday.Should().BeEqualTo(10000m);
        await yesterdayPage.RepaidToday.Should().BeEqualTo(4000m);
    }

    [Test]
    public async Task ReadAsync_CashierWithoutPermission_ThrowsUnauthorizedAccessException()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        RolePermissionService.SetCache(new Dictionary<(string RoleName, string PolicyName), bool>
        {
            [(AppRoles.InventoryManager, AppPolicies.ManageDebt)] = false
        });

        db.Roles.Add(new() { Id = 2, Name = AppRoles.InventoryManager, NormalizedName = "INVENTORYMANAGER" });
        db.Users.Add(new() { Id = 2, UserName = "cashier1", NormalizedUserName = "CASHIER1" });
        db.UserRoles.Add(new() { RoleId = 2, UserId = 2 });
        await db.SaveChangesAsync();

        var cashier = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, "2"),
            new(ClaimTypes.Name, "cashier1"),
            new(ClaimTypes.Role, AppRoles.InventoryManager)
        ], "test"));

        var service = new DebtService(scopes, config);
        var act = async () => await service.ReadAsync(cashier);
        await Assert.That(act).Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task ReadAsync_CashierWithGrantedPermission_Succeeds()
    {
        var (scopes, config, db) = CreateTestEnvironment(Guid.NewGuid().ToString());
        RolePermissionService.SetCache(new Dictionary<(string RoleName, string PolicyName), bool>
        {
            [(AppRoles.InventoryManager, AppPolicies.ManageDebt)] = true
        });

        db.Roles.Add(new() { Id = 2, Name = AppRoles.InventoryManager, NormalizedName = "INVENTORYMANAGER" });
        db.Users.Add(new() { Id = 2, UserName = "cashier1", NormalizedUserName = "CASHIER1" });
        db.UserRoles.Add(new() { RoleId = 2, UserId = 2 });
        db.RolePermissions.Add(new()
        {
            RoleName = AppRoles.InventoryManager,
            PolicyName = AppPolicies.ManageDebt,
            IsGranted = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var cashier = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, "2"),
            new(ClaimTypes.Name, "cashier1"),
            new(ClaimTypes.Role, AppRoles.InventoryManager)
        ], "test"));

        var service = new DebtService(scopes, config);
        var page = await service.ReadAsync(cashier);
        await page.Should().NotBeNull();
    }
}

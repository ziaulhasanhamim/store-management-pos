using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;

namespace POSApp.Tests.Unit.Services;

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
            WorkingDate = new DateOnly(2026, 11, 24)
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
}

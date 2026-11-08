using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Reports;

public class ExpenseReportTests
{
    [Test]
    public async Task LocationExpenseSummary_CalculatesNetImpactAndShareCorrectly()
    {
        var summary = new LocationExpenseSummaryDto(
            LocationId: 1,
            LocationName: "Main Store",
            TotalExpense: 20000m,
            SharePercentage: 40.0m,
            TopCategoryName: "Rent",
            TopCategoryAmount: 15000m,
            ProcessingFees: 400m,
            SalesRevenue: 100000m,
            NetExpenseImpact: 80000m);

        await summary.LocationName.Should().BeEqualTo("Main Store");
        await summary.TotalExpense.Should().BeEqualTo(20000m);
        await summary.SharePercentage.Should().BeEqualTo(40.0m);
        await summary.NetExpenseImpact.Should().BeEqualTo(summary.SalesRevenue - summary.TotalExpense);
        await summary.ProcessingFees.Should().BeEqualTo(400m);
    }

    [Test]
    public async Task ExpenseCategoryTotal_SupportsDynamicCategoryIdsAndNames()
    {
        var cat = new ExpenseCategoryTotal(
            CategoryId: 42,
            Name: "Custom Packaging & Boxes",
            Amount: 3200m);

        await cat.CategoryId.Should().BeEqualTo(42);
        await cat.Name.Should().BeEqualTo("Custom Packaging & Boxes");
        await cat.Amount.Should().BeEqualTo(3200m);
    }

    [Test]
    public async Task ExpenseReportDto_AggregatesTotalsAcrossStores()
    {
        var range = new ReportRange(
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7),
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        var loc1 = new LocationExpenseSummaryDto(1, "Main Store", 30000m, 60.0m, "Rent", 25000m, 500m, 120000m, 90000m);
        var loc2 = new LocationExpenseSummaryDto(2, "Branch Store", 20000m, 40.0m, "Snacks", 5000m, 200m, 60000m, 40000m);

        var report = new ExpenseReportDto(
            Range: range,
            TotalExpenses: 50000m,
            TopLocationName: "Main Store",
            TopLocationAmount: 30000m,
            TopLocationPercentage: 60.0m,
            TopCategoryName: "Rent",
            TopCategoryAmount: 25000m,
            TopCategoryPercentage: 50.0m,
            ProcessingFeesTotal: 700m,
            Locations: [loc1, loc2],
            Categories: [new CategoryExpenseSummaryDto(1, "Rent", 25000m, 50.0m)],
            Items: []);

        await report.TotalExpenses.Should().BeEqualTo(50000m);
        await report.Locations.Count.Should().BeEqualTo(2);
        await report.Locations.Sum(l => l.TotalExpense).Should().BeEqualTo(report.TotalExpenses);
        await report.ProcessingFeesTotal.Should().BeEqualTo(700m);
        await report.TopLocationName.Should().BeEqualTo("Main Store");
    }

    [Test]
    public async Task DeleteAsync_ExpenseOnDifferentWorkingDay_ThrowsValidationException()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<POSApp.Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new POSApp.Persistence.AppDbContext(options);

        var adminRole = new Microsoft.AspNetCore.Identity.IdentityRole<int> { Id = 1, Name = "Admin", NormalizedName = "ADMIN" };
        var userRole = new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { RoleId = 1, UserId = 1 };
        db.Roles.Add(adminRole);
        db.UserRoles.Add(userRole);

        // Current working day is 2026-11-08
        var today = new DateOnly(2026, 11, 8);
        db.WorkingDays.Add(new POSApp.Persistence.Entities.WorkingDay { Id = 1, Date = today });

        // Expense occurred on previous working day: 2026-11-07
        var pastExpense = new POSApp.Persistence.Entities.Expense
        {
            Id = 50,
            Amount = 100m,
            Method = PaymentMethod.BankTransfer,
            OccurredAt = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero),
            Notes = "Past day expense",
            CreatedByUserId = 1
        };
        db.Expenses.Add(pastExpense);
        await db.SaveChangesAsync();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped(_ => db);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusinessTimeZone"] = "Asia/Dhaka"
        }).Build();

        var service = new POSApp.Services.MoneyEntryService(scopeFactory, config);
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1"),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
                }, "test"));

        // Voiding an expense from another day must throw ValidationException
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.DeleteAsync(principal, kind: "expenses", id: 50));
    }

    [Test]
    public async Task DeleteAsync_ExpenseOnSameWorkingDay_ReversesBankAndDeletesExpense()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<POSApp.Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var today = new DateOnly(2026, 11, 8);
        using (var setupDb = new POSApp.Persistence.AppDbContext(options))
        {
            var adminRole = new Microsoft.AspNetCore.Identity.IdentityRole<int> { Id = 1, Name = "Admin", NormalizedName = "ADMIN" };
            var userRole = new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { RoleId = 1, UserId = 1 };
            setupDb.Roles.Add(adminRole);
            setupDb.UserRoles.Add(userRole);

            setupDb.WorkingDays.Add(new POSApp.Persistence.Entities.WorkingDay { Id = 1, Date = today });
            setupDb.DayFinances.Add(new POSApp.Persistence.Entities.DayFinance { Date = today, ClosedAt = null });

            var bankAccount = new POSApp.Persistence.Entities.BankAccount { Id = 1, Code = "MAIN", CurrentBalance = 5000m };
            setupDb.BankAccounts.Add(bankAccount);

            var expense = new POSApp.Persistence.Entities.Expense
            {
                Id = 60,
                Amount = 250m,
                Method = PaymentMethod.BankTransfer,
                OccurredAt = new DateTimeOffset(2026, 11, 8, 11, 0, 0, TimeSpan.Zero),
                Notes = "Office supplies",
                CreatedByUserId = 1
            };
            var bankMovement = new POSApp.Persistence.Entities.BankMovement
            {
                Id = 1,
                BankAccount = bankAccount,
                BankAccountId = 1,
                BusinessDate = today,
                Kind = CashMovementKind.Expense,
                Method = PaymentMethod.BankTransfer,
                Amount = -250m,
                Expense = expense,
                ExpenseId = 60,
                Notes = "Office supplies expense",
                CreatedByUserId = 1
            };
            setupDb.Expenses.Add(expense);
            setupDb.BankMovements.Add(bankMovement);
            await setupDb.SaveChangesAsync();
        }

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDbContext<POSApp.Persistence.AppDbContext>(b =>
            b.UseInMemoryDatabase(databaseName: dbName)
             .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BusinessTimeZone"] = "Asia/Dhaka"
        }).Build();

        var service = new POSApp.Services.MoneyEntryService(scopeFactory, config);
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1"),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
                }, "test"));

        // Void the expense
        await service.DeleteAsync(principal, kind: "expenses", id: 60);

        // Verification db context
        using var verifyDb = new POSApp.Persistence.AppDbContext(options);

        // Expense should be marked voided (not physically deleted) with intact FK history
        var remainingExpense = await verifyDb.Expenses.FindAsync(60);
        await remainingExpense.Should().NotBeNull();
        await remainingExpense!.IsVoided.Should().BeTrue();
        await remainingExpense.VoidedAt.HasValue.Should().BeTrue();

        // Bank balance should be restored: 5000 + 250 = 5250
        var updatedBank = await verifyDb.BankAccounts.FindAsync(1);
        await updatedBank!.CurrentBalance.Should().BeEqualTo(5250m);

        // Reversal bank movement should exist
        var reversal = await verifyDb.BankMovements.SingleOrDefaultAsync(m => m.ReversesMovementId == 1);
        await reversal.Should().NotBeNull();
        await reversal!.Amount.Should().BeEqualTo(250m);
    }
}

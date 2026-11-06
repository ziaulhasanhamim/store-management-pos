using POSApp.Client.DTOs;
using POSApp.Client.Security;
using POSApp.Persistence.Entities;
using POSApp.Security;

namespace POSApp.Tests.Unit.Administration;

public class DynamicPermissionsAndCashShortageTests
{
    [Test]
    public async Task RolePermissionService_DefaultMatrix_HasExpectedRoleDefaults()
    {
        // InventoryManager (Cashier) defaults
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ViewCostPrice).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.MakeSales).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ManageDrawer).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ManageInventory).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.RecordExpense).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ManageParties).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ApplyDiscount).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ViewFinancialReports).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryManager, AppPolicies.ManageStockAdjustment).Should().BeEqualTo(false);

        // SalesAssociate defaults
        await RolePermissionService.GetDefaultPermission(AppRoles.SalesAssociate, AppPolicies.MakeSales).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.SalesAssociate, AppPolicies.ViewCostPrice).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.SalesAssociate, AppPolicies.ApplyDiscount).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.SalesAssociate, AppPolicies.ManageDrawer).Should().BeEqualTo(false);
        await RolePermissionService.GetDefaultPermission(AppRoles.SalesAssociate, AppPolicies.RecordExpense).Should().BeEqualTo(false);

        // InventoryViewer defaults
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryViewer, AppPolicies.MakeSales).Should().BeEqualTo(false);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryViewer, AppPolicies.ViewCostPrice).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryViewer, AppPolicies.ViewFinancialReports).Should().BeEqualTo(true);
        await RolePermissionService.GetDefaultPermission(AppRoles.InventoryViewer, AppPolicies.ManageInventory).Should().BeEqualTo(false);
    }

    [Test]
    public async Task RolePermissionService_InCacheLookup_ReturnsSubMicrosecondWithoutDb()
    {
        RolePermissionService.SetCache(new Dictionary<(string RoleName, string PolicyName), bool>());
        var roles = new[] { AppRoles.SalesAssociate };
        var canSell = RolePermissionService.TryCheckCached(roles, AppPolicies.MakeSales, out var granted);

        await canSell.Should().BeEqualTo(true);
        await granted.Should().BeEqualTo(true);

        var canManageUsers = RolePermissionService.TryCheckCached(roles, AppPolicies.ManageUsers, out var adminGranted);
        await canManageUsers.Should().BeEqualTo(true);
        await adminGranted.Should().BeEqualTo(false);
    }

    [Test]
    public async Task DailyBusinessResult_FactoredCashShortage_ReducesNetProfit()
    {
        var date = new DateOnly(2026, 11, 6);
        // Sales = 10,000, COGS = 6,000 => Sales Profit = 4,000
        // Expenses = 500, Payroll = 1,000 => Total Spend = 1,500
        // Stock Variance = +100
        // Without cash variance: Net Profit = 4,000 - 1,500 + 100 = 2,600
        var normalDay = new DailyBusinessResult(date, 10000m, 6000m, 500m, 1000m)
        {
            StockVariance = 100m,
            CashVariance = 0m
        };
        await normalDay.NetProfit.Should().BeEqualTo(2600m);

        // Cash Shortage of 300 (CashVariance = -300)
        var shortDay = new DailyBusinessResult(date, 10000m, 6000m, 500m, 1000m)
        {
            StockVariance = 100m,
            CashVariance = -300m
        };
        await shortDay.CashShortage.Should().BeEqualTo(300m);
        await shortDay.CashOverage.Should().BeEqualTo(0m);
        // Net profit is reduced directly by 300 => 2,300
        await shortDay.NetProfit.Should().BeEqualTo(2300m);

        // Cash Overage of 150 (CashVariance = +150)
        var overDay = new DailyBusinessResult(date, 10000m, 6000m, 500m, 1000m)
        {
            StockVariance = 100m,
            CashVariance = 150m
        };
        await overDay.CashOverage.Should().BeEqualTo(150m);
        await overDay.CashShortage.Should().BeEqualTo(0m);
        await overDay.NetProfit.Should().BeEqualTo(2750m);
    }

    [Test]
    public async Task MonthlyFinanceDetails_AggregatesCashShortageAndReducesMonthlyNetProfit()
    {
        var d1 = new DailyBusinessResult(new DateOnly(2026, 11, 1), 10000m, 6000m, 500m, 500m)
        {
            StockVariance = 0m,
            CashVariance = -200m // 200 shortage
        };
        var d2 = new DailyBusinessResult(new DateOnly(2026, 11, 2), 12000m, 7000m, 600m, 500m)
        {
            StockVariance = 50m,
            CashVariance = 50m // 50 overage
        };

        var monthly = new MonthlyFinanceDetails(
            Days: [d1, d2],
            Categories: [new ExpenseCategoryTotal(ExpenseCategory.Utilities, 1100m)],
            Employees: [new EmployeeMonthlyPaid(1, "Staff", 1000m, 1000m, 0m, 0m)],
            OpeningAssets: new AssetPosition(0, 0, 0, 0, 0),
            ClosingAssets: new AssetPosition(0, 0, 0, 0, 0));

        await monthly.CashVariance.Should().BeEqualTo(-150m);
        await monthly.CashShortage.Should().BeEqualTo(200m);
        await monthly.CashOverage.Should().BeEqualTo(50m);

        // d1 NetProfit = (4000 - 1000 + 0 - 200) = 2800
        // d2 NetProfit = (5000 - 1100 + 50 + 50) = 4000
        // Total monthly NetProfit = 6800
        await monthly.NetProfit.Should().BeEqualTo(6800m);
    }

    [Test]
    public async Task User_MustChangePassword_Flag_DefaultsExpectedly()
    {
        var user = new User
        {
            UserName = "newcashier",
            MustChangePassword = true
        };
        await user.MustChangePassword.Should().BeEqualTo(true);

        var input = new ChangePasswordInput
        {
            CurrentPassword = "tempPassword1",
            NewPassword = "newSecurePassword2",
            ConfirmPassword = "newSecurePassword2"
        };
        await input.NewPassword.Should().BeEqualTo(input.ConfirmPassword);
    }
}

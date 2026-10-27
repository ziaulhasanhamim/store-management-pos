using POSApp.Client.DTOs;
using POSApp.Client.Enums;

namespace POSApp.Tests.Unit.Reports;

public class MonthlyFinanceDetailsTests
{
    [Test]
    public async Task DailyBusinessResult_ComputesMarginsAndSpendCorrectly()
    {
        var day = new DailyBusinessResult(
            Date: new DateOnly(2026, 10, 3),
            Sales: 23500m,
            Cogs: 17686m,
            Expenses: 1100m,
            Payroll: 500m)
        {
            StockVariance = -100m,
            SupplierPaid = 23000m,
            StockReceived = 15000m,
            IsWorkingDay = true,
            ExpenseCategories =
            [
                new ExpenseCategoryTotal(ExpenseCategory.Refreshments, 400m) { Name = "Snacks" },
                new ExpenseCategoryTotal(ExpenseCategory.Transport, 700m) { Name = "Conveyance" }
            ]
        };

        await day.SalesProfit.Should().BeEqualTo(5814m);
        await day.TotalSpend.Should().BeEqualTo(1600m);
        await day.NetProfit.Should().BeEqualTo(5814m - 1600m - 100m); // 4114m
        await day.SupplierPaid.Should().BeEqualTo(23000m);
        await day.StockReceived.Should().BeEqualTo(15000m);
        await day.ExpenseCategories.Count.Should().BeEqualTo(2);
        await day.IsWorkingDay.Should().BeEqualTo(true);
    }

    [Test]
    public async Task MonthlyFinanceDetails_AggregatesWorkingDaysAndAssetGrowth()
    {
        var day1 = new DailyBusinessResult(
            Date: new DateOnly(2026, 10, 3),
            Sales: 20000m,
            Cogs: 15000m,
            Expenses: 1000m,
            Payroll: 500m)
        {
            SupplierPaid = 12000m,
            StockReceived = 8000m
        };

        var day2 = new DailyBusinessResult(
            Date: new DateOnly(2026, 10, 4),
            Sales: 30000m,
            Cogs: 22000m,
            Expenses: 1500m,
            Payroll: 500m)
        {
            SupplierPaid = 10000m,
            StockReceived = 14000m
        };

        var categories = new List<ExpenseCategoryTotal>
        {
            new(ExpenseCategory.Refreshments, 1000m),
            new(ExpenseCategory.Transport, 1500m)
        };

        var employees = new List<EmployeeMonthlyPaid>
        {
            new(1, "Hamim", 15000m, 1000m, 0m, 0m)
        };

        var opening = new AssetPosition(
            Stock: 50000m,
            Cash: 10000m,
            Bank: 20000m,
            SupplierDue: 15000m,
            CustomerDue: 5000m)
        {
            Debt = 2000m
        };

        var closing = new AssetPosition(
            Stock: 56000m,
            Cash: 14000m,
            Bank: 22000m,
            SupplierDue: 12000m,
            CustomerDue: 7000m)
        {
            Debt = 1000m
        };

        var monthly = new MonthlyFinanceDetails(
            Days: [day1, day2],
            Categories: categories,
            Employees: employees,
            OpeningAssets: opening,
            ClosingAssets: closing);

        // Assets
        // Opening NetAssets: 50000 + 10000 + 20000 - 15000 + 5000 - 2000 = 68000
        await opening.NetAssets.Should().BeEqualTo(68000m);
        // Closing NetAssets: 56000 + 14000 + 22000 - 12000 + 7000 - 1000 = 86000
        await closing.NetAssets.Should().BeEqualTo(86000m);
        // Asset Built: 86000 - 68000 = 18000
        await monthly.AssetGrowth.Should().BeEqualTo(18000m);

        // Operational totals
        await monthly.TotalSales.Should().BeEqualTo(50000m);
        await monthly.SalesProfit.Should().BeEqualTo(13000m);
        await monthly.TotalSupplierPaid.Should().BeEqualTo(22000m);
        await monthly.TotalStockReceived.Should().BeEqualTo(22000m);
        await monthly.Expenses.Should().BeEqualTo(2500m);
        await monthly.Payroll.Should().BeEqualTo(1000m);
        await monthly.TotalSpend.Should().BeEqualTo(3500m);
        await monthly.NetProfit.Should().BeEqualTo(9500m);
    }

    [Test]
    public async Task FinanceExportService_GeneratesValidExcelWorkbook()
    {
        var day = new DailyBusinessResult(
            Date: new DateOnly(2026, 10, 3),
            Sales: 25000m,
            Cogs: 18000m,
            Expenses: 1200m,
            Payroll: 800m)
        {
            SupplierPaid = 15000m,
            StockReceived = 20000m
        };

        var monthly = new MonthlyFinanceDetails(
            Days: [day],
            Categories: [new(ExpenseCategory.Refreshments, 1200m)],
            Employees: [new(1, "Hamim", 10000m, 800m, 0m, 0m)],
            OpeningAssets: new(50000m, 10000m, 20000m, 15000m, 5000m),
            ClosingAssets: new(55000m, 12000m, 25000m, 10000m, 6000m))
        {
            Suppliers =
            [
                new SupplierMonthlySummary(1, "Key Supplier Ltd", 20000m, 15000m, 35000m)
            ],
            LocationBreakdown =
            [
                new LocationFinanceSummary(1, "Main Store", 18000m, 13000m, 5000m, 12000m, 35000m),
                new LocationFinanceSummary(2, "Branch 2", 7000m, 5000m, 2000m, 8000m, 20000m)
            ]
        };

        var totals = new ClosingTotals(
            Start: new DateOnly(2026, 10, 1),
            End: new DateOnly(2026, 10, 31),
            OpeningCash: 10000m,
            OpeningBank: 20000m,
            CashIn: 25000m,
            CashOut: 17000m,
            BankIn: 5000m,
            BankOut: 0m,
            ClosingCash: 12000m,
            ClosingBank: 25000m,
            Revenue: 25000m,
            Cogs: 18000m,
            Expenses: 1200m,
            Salaries: 800m,
            SupplierPaid: 15000m,
            PayrollPaid: 800m,
            SupplierDue: 10000m,
            CustomerDue: 6000m,
            StockValue: 55000m,
            ClosedAt: null,
            Notes: "Test Notes")
        {
            Monthly = monthly
        };

        var bytes = POSApp.Services.FinanceExportService.GenerateMonthExcel(totals, "BDT");
        await bytes.Length.Should().BeGreaterThan(0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        await workbook.Worksheets.Count.Should().BeEqualTo(6);
        await workbook.Worksheets.Contains("Overview").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Stock Locations").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Working Days").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Suppliers").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Expenses & Payroll").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Balance Sheet").Should().BeEqualTo(true);

        var overviewSheet = workbook.Worksheet("Overview");
        await overviewSheet.Cell("A1").GetString().Should().BeEqualTo("RETAILPOS");

        var locationsSheet = workbook.Worksheet("Stock Locations");
        await locationsSheet.Cell("A2").GetString().Should().BeEqualTo("Main Store");
        await locationsSheet.Cell("B2").GetValue<decimal>().Should().BeEqualTo(18000m);
        await locationsSheet.Cell("A3").GetString().Should().BeEqualTo("Branch 2");
        await locationsSheet.Cell("A4").GetString().Should().BeEqualTo("Total (2 locations)");
        await locationsSheet.Cell("B4").FormulaA1.Should().BeEqualTo("SUM(B2:B3)");

        var suppliersSheet = workbook.Worksheet("Suppliers");
        await suppliersSheet.Cell("A2").GetString().Should().BeEqualTo("Key Supplier Ltd");

        // Now verify location-scoped export
        var locationScopedTotals = totals with
        {
            SelectedLocationId = 1,
            SelectedLocationName = "Main Store"
        };
        var scopedBytes = POSApp.Services.FinanceExportService.GenerateMonthExcel(locationScopedTotals, "BDT");
        using var scopedStream = new MemoryStream(scopedBytes);
        using var scopedWorkbook = new ClosedXML.Excel.XLWorkbook(scopedStream);
        await scopedWorkbook.Worksheets.Contains("Stock Locations").Should().BeEqualTo(false);
        var scopedOverview = scopedWorkbook.Worksheet("Overview");
        await scopedOverview.Cell("A2").GetString().Should().Contain("Main Store");
        await scopedOverview.Cell("A3").GetString().Should().Contain("Location: Main Store");
    }

    [Test]
    public async Task GenerateDayExcel_GeneratesOverviewDrawersExpensesAndPayments()
    {
        var dayDate = new DateOnly(2026, 10, 27);
        var totals = new ClosingTotals(
            Start: dayDate,
            End: dayDate,
            OpeningCash: 5000m,
            OpeningBank: 15000m,
            CashIn: 8000m,
            CashOut: 3000m,
            BankIn: 2000m,
            BankOut: 0m,
            ClosingCash: 10000m,
            ClosingBank: 17000m,
            Revenue: 9000m,
            Cogs: 6000m,
            Expenses: 500m,
            Salaries: 200m,
            SupplierPaid: 2000m,
            PayrollPaid: 200m,
            SupplierDue: 8000m,
            CustomerDue: 4000m,
            StockValue: 35000m,
            ClosedAt: DateTimeOffset.UtcNow,
            Notes: "Day test")
        {
            CountedCash = 9950m,
            Difference = -50m,
            Locations =
            [
                new LocationCashClosing("Main Store", 1, "Closed", 5000m, 10000m, 9950m, -50m)
            ],
            Daily = new DailyFinanceDetails(
                Categories:
                [
                    new ExpenseCategoryTotal(ExpenseCategory.Transport, 300m) { Name = "Transport" },
                    new ExpenseCategoryTotal(ExpenseCategory.Refreshments, 200m) { Name = "Refreshments" }
                ],
                Payments:
                [
                    new DayPayment(DateTimeOffset.UtcNow, "Sale", "Walk-in", "Cash drawer", PaymentMethod.Cash, 1500m, "Bill #101"),
                    new DayPayment(DateTimeOffset.UtcNow, "Expense", "Local Shop", "Cash drawer", PaymentMethod.Cash, -200m, "Snacks")
                ]
            )
        };

        var bytes = POSApp.Services.FinanceExportService.GenerateDayExcel(totals, "BDT", "RetailPOS");
        await bytes.Length.Should().BeGreaterThan(0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        await workbook.Worksheets.Contains("Day Overview").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Cash Drawers").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Expense Categories").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Itemized Transactions").Should().BeEqualTo(true);

        var overview = workbook.Worksheet("Day Overview");
        await overview.Cell("A1").GetString().Should().BeEqualTo("RETAILPOS");
        await overview.Cell("A2").GetString().Should().Contain("Daily Financial Closing Report");

        var drawers = workbook.Worksheet("Cash Drawers");
        await drawers.Cell("A2").GetString().Should().BeEqualTo("Main Store");
        await drawers.Cell("B2").GetString().Should().BeEqualTo("Closed");
        await drawers.Cell("F2").GetValue<decimal>().Should().BeEqualTo(-50m);

        var expenses = workbook.Worksheet("Expense Categories");
        await expenses.Cell("A2").GetString().Should().BeEqualTo("Transport");
        await expenses.Cell("B2").GetValue<decimal>().Should().BeEqualTo(300m);

        var payments = workbook.Worksheet("Itemized Transactions");
        await payments.Cell("B2").GetString().Should().BeEqualTo("Sale");
        await payments.Cell("F2").GetValue<decimal>().Should().BeEqualTo(1500m);
    }
}

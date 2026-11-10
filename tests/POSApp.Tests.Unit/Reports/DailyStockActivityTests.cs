using System.IO;
using ClosedXML.Excel;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Services;

namespace POSApp.Tests.Unit.Reports;

public class DailyStockActivityTests
{
    [Test]
    public async Task DailyStockActivity_ComputesTotalsAndNetChangesAccurately()
    {
        var now = DateTimeOffset.UtcNow;
        var inward = new List<DailyStockInItem>
        {
            new(now, 1, "PO-1001", "INV-500", "Oraimo Godhuli", "Main Store", 3, 30m, 15000m, "Batch 1"),
            new(now.AddMinutes(30), 2, "PO-1002", null, "Deximpo Godhuli", "Main Store", 2, 20m, 8000m, null)
        };

        var outward = new List<DailyStockOutItem>
        {
            new(now.AddHours(1), "Sale", "Bill #101 · Walk-in (2 items)", "Main Store", 10m, 6000m, "Sale total: 9,000.00"),
            new(now.AddHours(2), "Damage / Waste", "Screen Protector · Cracked during handling", "Main Store", 2m, 500m, "Damaged item")
        };

        var activity = new DailyStockActivity(
            TotalStockInValue: inward.Sum(i => i.TotalAmount),
            TotalStockInUnits: inward.Sum(i => i.TotalQuantity),
            TotalStockOutValue: outward.Sum(o => o.TotalCostValue),
            TotalStockOutUnits: outward.Sum(o => o.TotalQuantity),
            NetStockValueChange: inward.Sum(i => i.TotalAmount) - outward.Sum(o => o.TotalCostValue),
            NetStockUnitsChange: inward.Sum(i => i.TotalQuantity) - outward.Sum(o => o.TotalQuantity),
            Inward: inward,
            Outward: outward);

        await activity.TotalStockInValue.Should().BeEqualTo(23000m);
        await activity.TotalStockInUnits.Should().BeEqualTo(50m);
        await activity.TotalStockOutValue.Should().BeEqualTo(6500m);
        await activity.TotalStockOutUnits.Should().BeEqualTo(12m);
        await activity.NetStockValueChange.Should().BeEqualTo(16500m);
        await activity.NetStockUnitsChange.Should().BeEqualTo(38m);
    }

    [Test]
    public async Task DayExcelExport_IncludesStockActivitySheet_WhenStockActivityPresent()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totals = new ClosingTotals(
            Start: today,
            End: today,
            OpeningCash: 5000m,
            OpeningBank: 10000m,
            CashIn: 8000m,
            CashOut: 3000m,
            BankIn: 2000m,
            BankOut: 1000m,
            ClosingCash: 10000m,
            ClosingBank: 11000m,
            Revenue: 12000m,
            Cogs: 7000m,
            Expenses: 1500m,
            Salaries: 1000m,
            SupplierPaid: 3000m,
            PayrollPaid: 1000m,
            SupplierDue: 25000m,
            CustomerDue: 8000m,
            StockValue: 150000m,
            ClosedAt: null,
            Notes: null)
        {
            Daily = new DailyFinanceDetails(
                Categories: [],
                Payments:
                [
                    new DayPayment(now, "Supplier payments", "Oraimo Godhuli", "Main Store", PaymentMethod.Cash, 15000m, "Purchase #1 · 30 units")
                    {
                        CustomMethod = "Stock In",
                        IsStockIn = true
                    },
                    new DayPayment(now.AddHours(1), "Supplier payments", "Oraimo Godhuli", "Cash drawer", PaymentMethod.Cash, -1000m, "Supplier payment #11")
                ])
            {
                StockActivity = new DailyStockActivity(
                    TotalStockInValue: 15000m,
                    TotalStockInUnits: 30m,
                    TotalStockOutValue: 7000m,
                    TotalStockOutUnits: 15m,
                    NetStockValueChange: 8000m,
                    NetStockUnitsChange: 15m,
                    Inward:
                    [
                        new DailyStockInItem(now, 1, "PO-1", "INV-1", "Oraimo Godhuli", "Main Store", 2, 30m, 15000m, "Received")
                    ],
                    Outward:
                    [
                        new DailyStockOutItem(now.AddHours(2), "Sale", "Bill #101 · Customer A", "Main Store", 15m, 7000m, "Sale")
                    ])
            }
        };

        var bytes = FinanceExportService.GenerateDayExcel(totals, "BDT", "RetailPOS");
        await bytes.Length.Should().BeGreaterThan(0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        await workbook.Worksheets.Contains("Stock Activity (In-Out)").Should().BeEqualTo(true);
        var ws = workbook.Worksheet("Stock Activity (In-Out)");

        await ws.Cell("A4").GetString().Should().Contain("Total Stock In");
        await ws.Cell("B4").GetValue<decimal>().Should().BeEqualTo(30m);
        await ws.Cell("C4").GetValue<decimal>().Should().BeEqualTo(15000m);

        await ws.Cell("A5").GetString().Should().Contain("Total Stock Out");
        await ws.Cell("B5").GetValue<decimal>().Should().BeEqualTo(15m);
        await ws.Cell("C5").GetValue<decimal>().Should().BeEqualTo(7000m);

        // Check Itemized Transactions sheet contains Stock In label
        var txWs = workbook.Worksheet("Itemized Transactions");
        await txWs.Cell("B2").GetString().Should().BeEqualTo("Stock In (Supplier)");
        await txWs.Cell("E2").GetString().Should().BeEqualTo("Stock In");
        await txWs.Cell("F2").GetValue<decimal>().Should().BeEqualTo(15000m);
    }
}

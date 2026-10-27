using System.Globalization;
using ClosedXML.Excel;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;

namespace POSApp.Services;

public static class FinanceExportService
{
    private const string CurrencyFormat = "#,##0.00";
    private static readonly XLColor BrandDark = XLColor.FromHtml("#0F172A");
    private static readonly XLColor BrandBlue = XLColor.FromHtml("#1E40AF");
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1E293B");
    private static readonly XLColor ZebraBg = XLColor.FromHtml("#F8FAFC");
    private static readonly XLColor TotalBg = XLColor.FromHtml("#E2E8F0");
    private static readonly XLColor SuccessGreen = XLColor.FromHtml("#047857");
    private static readonly XLColor DangerRed = XLColor.FromHtml("#B91C1C");

    public static byte[] GenerateMonthExcel(ClosingTotals totals, string currencyCode = "BDT", string storeName = "RetailPOS")
    {
        using var workbook = new XLWorkbook();
        var monthName = totals.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        BuildSummarySheet(workbook, totals, monthName, currencyCode, storeName);
        if (totals.SelectedLocationId == null && (totals.LocationBreakdown.Count > 0 || (totals.Monthly?.LocationBreakdown.Count ?? 0) > 0))
        {
            BuildLocationsSheet(workbook, totals, currencyCode);
        }
        BuildWorkingDaysSheet(workbook, totals, currencyCode);
        BuildSuppliersSheet(workbook, totals, currencyCode);
        BuildExpensesPayrollSheet(workbook, totals, currencyCode);
        BuildAssetsSheet(workbook, totals, currencyCode);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] GenerateDayExcel(ClosingTotals totals, string currencyCode = "BDT", string storeName = "RetailPOS")
    {
        using var workbook = new XLWorkbook();
        var dateStr = totals.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        BuildDaySummarySheet(workbook, totals, dateStr, currencyCode, storeName);
        if (totals.SelectedLocationId == null && totals.LocationBreakdown.Count > 0)
        {
            BuildLocationsSheet(workbook, totals, currencyCode);
        }
        if (totals.Locations.Count > 0)
        {
            BuildDayDrawersSheet(workbook, totals, currencyCode);
        }
        if (totals.Daily?.Categories.Count > 0)
        {
            BuildDayExpensesSheet(workbook, totals, currencyCode);
        }
        if (totals.Daily?.Payments.Count > 0)
        {
            BuildDayPaymentsSheet(workbook, totals, currencyCode);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildDaySummarySheet(XLWorkbook workbook, ClosingTotals totals, string dateStr, string currency, string storeName)
    {
        var ws = workbook.Worksheets.Add("Day Overview");
        ws.ShowGridLines = true;

        ws.Cell("A1").Value = (storeName ?? "RetailPOS").ToUpperInvariant();
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.FontColor = BrandBlue;

        var subtitle = totals.SelectedLocationName != null
            ? $"Daily Financial Closing Report · {totals.SelectedLocationName} · {dateStr}"
            : $"Daily Financial Closing Report · {dateStr}";
        ws.Cell("A2").Value = subtitle;
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 12;
        ws.Cell("A2").Style.Font.FontColor = BrandDark;

        var locationTag = totals.SelectedLocationName != null ? $"Location: {totals.SelectedLocationName} · " : "";
        ws.Cell("A3").Value = $"{locationTag}Status: {(totals.ClosedAt is null ? "Provisional (Open)" : $"Closed at {totals.ClosedAt:yyyy-MM-dd HH:mm}")} · Currency: {currency}";
        ws.Cell("A3").Style.Font.Italic = true;
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Cell("A3").Style.Font.FontColor = XLColor.SlateGray;

        var r = 5;

        void DrawKpiTable(string title, (string Label, decimal Value, bool Highlight, bool IsNegativeGood)[] rows)
        {
            ws.Cell(r, 1).Value = title;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 11;
            ws.Cell(r, 1).Style.Font.FontColor = BrandBlue;
            r++;

            var header = ws.Range(r, 1, r, 2);
            ws.Cell(r, 1).Value = "Metric";
            ws.Cell(r, 2).Value = $"Amount ({currency})";
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = BrandDark;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            r++;

            foreach (var row in rows)
            {
                ws.Cell(r, 1).Value = row.Label;
                ws.Cell(r, 2).Value = row.Value;
                ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;

                if (row.Highlight)
                {
                    ws.Range(r, 1, r, 2).Style.Font.Bold = true;
                    ws.Range(r, 1, r, 2).Style.Fill.BackgroundColor = TotalBg;
                    ws.Cell(r, 2).Style.Font.FontColor = (row.Value >= 0 != row.IsNegativeGood) ? SuccessGreen : DangerRed;
                }
                r++;
            }
            r++;
        }

        var grossProfit = totals.Revenue - totals.Cogs;
        var operatingExpenses = totals.Expenses + totals.Salaries;
        var netProfit = grossProfit - operatingExpenses + totals.StockVariance;

        DrawKpiTable("Trading & Profitability",
        [
            ("Gross Sales Revenue", totals.Revenue, false, false),
            ("Cost of Goods Sold (COGS)", -totals.Cogs, false, true),
            ("Gross Profit on Sales", grossProfit, true, false),
            ("Operating Expenses", -totals.Expenses, false, true),
            ("Staff Salaries / Payroll", -totals.Salaries, false, true),
            ("Stock Adjustment / Variance", totals.StockVariance, false, false),
            ("Net Operating Profit", netProfit, true, false)
        ]);

        var totalInflows = totals.CashIn + totals.BankIn;
        var totalOutflows = totals.CashOut + totals.BankOut;
        var netCashFlow = totalInflows - totalOutflows;

        DrawKpiTable("Cash & Bank Liquidity",
        [
            ("Opening Drawer Cash", totals.OpeningCash, false, false),
            ("Opening Bank Balance", totals.OpeningBank, false, false),
            ("Total Inflows (Cash & Bank)", totalInflows, false, false),
            ("Total Outflows (Cash & Bank)", -totalOutflows, false, true),
            ("Net Day Cash Flow", netCashFlow, true, false),
            ("Expected Closing Cash", totals.ClosingCash, false, false),
            ("Expected Closing Bank", totals.ClosingBank, false, false),
            ("Counted Cash (Drawers)", totals.CountedCash ?? totals.ClosingCash, false, false),
            ("Cash Variance / Drawer Discrepancy", totals.Difference ?? totals.CashVariance, true, false)
        ]);

        DrawKpiTable("Receivables & Payables Position",
        [
            ("Outstanding Customer Due (Receivable)", totals.CustomerDue, false, false),
            ("Outstanding Supplier Due (Payable)", totals.SupplierDue, false, true),
            ("Outstanding Lender Debt (Payable)", totals.OutstandingDebt, false, true),
            ("Closing Stock Valuation", totals.StockValue, true, false)
        ]);

        ws.Columns(1, 2).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 40);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 22);
    }

    private static void BuildDayDrawersSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var ws = workbook.Worksheets.Add("Cash Drawers");
        ws.ShowGridLines = true;

        ws.Cell("A1").Value = "Stock Location / Drawer";
        ws.Cell("B1").Value = "Status";
        ws.Cell("C1").Value = "Opening Cash";
        ws.Cell("D1").Value = "Expected Cash";
        ws.Cell("E1").Value = "Counted Cash";
        ws.Cell("F1").Value = "Discrepancy";

        var header = ws.Range("A1:F1");
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = BrandDark;
        for (var c = 3; c <= 6; c++)
        {
            ws.Cell(1, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        var r = 2;
        foreach (var drawer in totals.Locations)
        {
            ws.Cell(r, 1).Value = drawer.Location;
            ws.Cell(r, 2).Value = drawer.Status;
            ws.Cell(r, 3).Value = drawer.Opening;
            ws.Cell(r, 4).Value = drawer.Expected;
            ws.Cell(r, 5).Value = drawer.Counted ?? drawer.Expected;
            ws.Cell(r, 6).Value = drawer.Difference ?? 0m;

            for (var c = 3; c <= 6; c++)
            {
                ws.Cell(r, c).Style.NumberFormat.Format = CurrencyFormat;
            }

            if ((drawer.Difference ?? 0m) != 0)
            {
                ws.Cell(r, 6).Style.Font.Bold = true;
                ws.Cell(r, 6).Style.Font.FontColor = (drawer.Difference ?? 0m) > 0 ? SuccessGreen : DangerRed;
            }

            r++;
        }

        ws.Columns(1, 6).AdjustToContents();
    }

    private static void BuildDayExpensesSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var categories = totals.Daily?.Categories ?? [];
        if (categories.Count == 0) return;

        var ws = workbook.Worksheets.Add("Expense Categories");
        ws.ShowGridLines = true;

        ws.Cell("A1").Value = "Expense Category";
        ws.Cell("B1").Value = $"Total Amount ({currency})";
        ws.Cell("C1").Value = "% of Total Spend";

        var header = ws.Range("A1:C1");
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = BrandDark;
        ws.Cell("B1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell("C1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var totalSpend = categories.Sum(c => c.Amount);
        var r = 2;

        foreach (var cat in categories.OrderByDescending(c => c.Amount))
        {
            ws.Cell(r, 1).Value = cat.Name;
            ws.Cell(r, 2).Value = cat.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(r, 3).Value = totalSpend > 0 ? (cat.Amount / totalSpend) : 0m;
            ws.Cell(r, 3).Style.NumberFormat.Format = "0.0%";
            r++;
        }

        ws.Cell(r, 1).Value = "Total Operating Spend";
        ws.Cell(r, 2).FormulaA1 = $"=SUM(B2:B{r - 1})";
        ws.Cell(r, 3).Value = 1.0m;
        ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;
        ws.Cell(r, 3).Style.NumberFormat.Format = "0.0%";

        var totalRange = ws.Range(r, 1, r, 3);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = TotalBg;
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

        ws.Columns(1, 3).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 32);
    }

    private static void BuildDayPaymentsSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var payments = totals.Daily?.Payments ?? [];
        if (payments.Count == 0) return;

        var ws = workbook.Worksheets.Add("Itemized Transactions");
        ws.ShowGridLines = true;

        ws.Cell("A1").Value = "Time";
        ws.Cell("B1").Value = "Type";
        ws.Cell("C1").Value = "Party / Description";
        ws.Cell("D1").Value = "Account";
        ws.Cell("E1").Value = "Payment Method";
        ws.Cell("F1").Value = $"Amount ({currency})";
        ws.Cell("G1").Value = "Notes";

        var header = ws.Range("A1:G1");
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = BrandDark;
        ws.Cell("F1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var r = 2;
        foreach (var p in payments.OrderBy(p => p.At))
        {
            ws.Cell(r, 1).Value = p.At.ToString("HH:mm:ss");
            ws.Cell(r, 2).Value = p.Type;
            ws.Cell(r, 3).Value = p.Party;
            ws.Cell(r, 4).Value = p.Account;
            ws.Cell(r, 5).Value = p.Method.ToString();
            ws.Cell(r, 6).Value = p.Amount;
            ws.Cell(r, 6).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(r, 7).Value = p.Notes ?? "";

            if (r % 2 == 1)
            {
                ws.Range(r, 1, r, 7).Style.Fill.BackgroundColor = ZebraBg;
            }
            r++;
        }

        ws.Cell(r, 1).Value = "Total Net Flow";
        ws.Cell(r, 6).FormulaA1 = $"=SUM(F2:F{r - 1})";
        ws.Cell(r, 6).Style.NumberFormat.Format = CurrencyFormat;

        var totalRange = ws.Range(r, 1, r, 7);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = TotalBg;
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

        ws.Columns(1, 7).AdjustToContents();
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 24);
        ws.Column(7).Width = Math.Max(ws.Column(7).Width, 30);
    }

    private static void BuildSummarySheet(XLWorkbook workbook, ClosingTotals totals, string monthName, string currency, string storeName)
    {
        var ws = workbook.Worksheets.Add("Overview");
        ws.ShowGridLines = true;

        // Title Block
        ws.Cell("A1").Value = (storeName ?? "RetailPOS").ToUpperInvariant();
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.FontColor = BrandBlue;

        var subtitle = totals.SelectedLocationName != null
            ? $"Monthly Financial Closing Report · {totals.SelectedLocationName} · {monthName}"
            : $"Monthly Financial Closing Report · {monthName}";
        ws.Cell("A2").Value = subtitle;
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 12;
        ws.Cell("A2").Style.Font.FontColor = BrandDark;

        var locationTag = totals.SelectedLocationName != null ? $"Location: {totals.SelectedLocationName} · " : "";
        ws.Cell("A3").Value = $"{locationTag}Status: {(totals.ClosedAt is null ? "Provisional (Open)" : $"Closed at {totals.ClosedAt:yyyy-MM-dd HH:mm}")} · Currency: {currency}";
        ws.Cell("A3").Style.Font.Italic = true;
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Cell("A3").Style.Font.FontColor = XLColor.SlateGray;

        var monthly = totals.Monthly;
        var r = 5;

        // Helper to draw a KPI summary table
        void DrawKpiTable(string title, (string Label, decimal Value, bool Highlight, bool IsNegativeGood)[] rows)
        {
            ws.Cell(r, 1).Value = title;
            ws.Range(r, 1, r, 2).Merge();
            ws.Cell(r, 1).Style.Fill.BackgroundColor = HeaderBg;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            r++;

            foreach (var row in rows)
            {
                ws.Cell(r, 1).Value = row.Label;
                ws.Cell(r, 2).Value = row.Value;
                ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;

                if (row.Highlight)
                {
                    ws.Cell(r, 1).Style.Font.Bold = true;
                    ws.Cell(r, 2).Style.Font.Bold = true;
                    if (row.Value >= 0)
                    {
                        ws.Cell(r, 2).Style.Font.FontColor = row.IsNegativeGood ? DangerRed : SuccessGreen;
                    }
                    else
                    {
                        ws.Cell(r, 2).Style.Font.FontColor = row.IsNegativeGood ? SuccessGreen : DangerRed;
                    }
                }
                r++;
            }
            r++;
        }

        DrawKpiTable("1. PRESENT OFFICE CASH & FUNDS",
        [
            ("Physical cash in location drawers", monthly?.ClosingAssets.Cash ?? totals.ClosingCash, false, false),
            ("Bank / digital balance (MAIN)", totals.ClosingBank, false, false),
            ("Total available closing funds", (monthly?.ClosingAssets.Cash ?? totals.ClosingCash) + totals.ClosingBank, true, false)
        ]);

        DrawKpiTable("2. DUE & PAY STATUS",
        [
            ("Total supplier payments made (tSupPayment)", monthly?.TotalSupplierPaid ?? totals.SupplierPaid, false, false),
            ("Total stock inventory received (tStok In)", monthly?.TotalStockReceived ?? totals.PurchaseTotal, false, false),
            ("Outstanding suppliers due (Payable)", monthly?.ClosingAssets.SupplierDue ?? totals.SupplierDue, true, true),
            ("Customer balance due (Receivable)", monthly?.ClosingAssets.CustomerDue ?? totals.CustomerDue, false, false)
        ]);

        DrawKpiTable("3. SALES STATUS & TRADING RESULTS",
        [
            ("Total net sales", monthly?.TotalSales ?? totals.Revenue, true, false),
            ("Cost of goods sold (COGS)", totals.Cogs, false, false),
            ("Sales profit (Gross profit)", monthly?.SalesProfit ?? (totals.Revenue - totals.Cogs), true, false),
            ("Operational expenses", monthly?.Expenses ?? totals.Expenses, false, true),
            ("Payroll payouts (Salaries & advances)", monthly?.Payroll ?? totals.PayrollPaid, false, true),
            ("Total spend (Expenses + Payroll)", (monthly?.Expenses ?? totals.Expenses) + (monthly?.Payroll ?? totals.PayrollPaid), false, true),
            ("Net trading profit", monthly?.NetProfit ?? (totals.Revenue - totals.Cogs - totals.Expenses - totals.Salaries), true, false)
        ]);

        if (monthly is not null)
        {
            DrawKpiTable("4. ASSET BUILT & EQUITY GROWTH",
            [
                ("Opening net assets (Month start)", monthly.OpeningAssets.NetAssets, false, false),
                ("Closing net assets (Month end)", monthly.ClosingAssets.NetAssets, false, false),
                ("Assets built this month (Net asset growth)", monthly.AssetGrowth, true, false)
            ]);
        }

        ws.Columns(1, 2).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 42);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 22);
    }

    private static void BuildLocationsSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var ws = workbook.Worksheets.Add("Stock Locations");
        ws.ShowGridLines = true;

        string[] headers =
        [
            "Store Location", "Net Sales", "Cost of Goods (COGS)", "Sales Profit",
            "Margin %", "Stock Received (tStok In)", "Inventory Value"
        ];

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = BrandDark;
            cell.Style.Alignment.Horizontal = col == 0 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
        }

        var locations = totals.Monthly?.LocationBreakdown.Count > 0
            ? totals.Monthly.LocationBreakdown
            : totals.LocationBreakdown;
        var row = 2;

        foreach (var loc in locations)
        {
            ws.Cell(row, 1).Value = loc.LocationName;
            ws.Cell(row, 2).Value = loc.Sales;
            ws.Cell(row, 3).Value = loc.Cogs;
            ws.Cell(row, 4).Value = loc.SalesProfit;
            ws.Cell(row, 5).FormulaA1 = $"=IF(B{row}>0, D{row}/B{row}, 0)";
            ws.Cell(row, 6).Value = loc.StockReceived;
            ws.Cell(row, 7).Value = loc.StockValue;

            ws.Cell(row, 2).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 3).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 4).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 5).Style.NumberFormat.Format = "0.0%";
            ws.Cell(row, 6).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 7).Style.NumberFormat.Format = CurrencyFormat;

            if (row % 2 == 0)
            {
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = ZebraBg;
            }

            row++;
        }

        if (locations.Count > 0)
        {
            ws.Cell(row, 1).Value = $"Total ({locations.Count} locations)";
            ws.Cell(row, 2).FormulaA1 = $"=SUM(B2:B{row - 1})";
            ws.Cell(row, 3).FormulaA1 = $"=SUM(C2:C{row - 1})";
            ws.Cell(row, 4).FormulaA1 = $"=SUM(D2:D{row - 1})";
            ws.Cell(row, 5).FormulaA1 = $"=IF(B{row}>0, D{row}/B{row}, 0)";
            ws.Cell(row, 6).FormulaA1 = $"=SUM(F2:F{row - 1})";
            ws.Cell(row, 7).FormulaA1 = $"=SUM(G2:G{row - 1})";

            ws.Cell(row, 2).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 3).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 4).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 5).Style.NumberFormat.Format = "0.0%";
            ws.Cell(row, 6).Style.NumberFormat.Format = CurrencyFormat;
            ws.Cell(row, 7).Style.NumberFormat.Format = CurrencyFormat;

            var totalRange = ws.Range(row, 1, row, 7);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = TotalBg;
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
        }

        ws.Columns(1, 7).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 28);
    }

    private static void BuildWorkingDaysSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var ws = workbook.Worksheets.Add("Working Days");
        ws.ShowGridLines = true;

        string[] headers =
        [
            "Date", "Day", "Sales", "COGS", "Sales profit", "Supplier paid",
            "Stock received", "Expenses", "Payroll paid", "Total spend", "Stock variance", "Net result"
        ];

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = BrandBlue;
            cell.Style.Alignment.Horizontal = col <= 1 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
        }

        var days = totals.Monthly?.Days ?? [];
        var row = 2;

        foreach (var day in days)
        {
            ws.Cell(row, 1).Value = day.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = day.Date.ToString("ddd");
            ws.Cell(row, 3).Value = day.Sales;
            ws.Cell(row, 4).Value = day.Cogs;
            ws.Cell(row, 5).Value = day.SalesProfit;
            ws.Cell(row, 6).Value = day.SupplierPaid;
            ws.Cell(row, 7).Value = day.StockReceived;
            ws.Cell(row, 8).Value = day.Expenses;
            ws.Cell(row, 9).Value = day.Payroll;
            ws.Cell(row, 10).Value = day.TotalSpend;
            ws.Cell(row, 11).Value = day.StockVariance;
            ws.Cell(row, 12).Value = day.NetProfit;

            for (var c = 3; c <= 12; c++)
            {
                ws.Cell(row, c).Style.NumberFormat.Format = CurrencyFormat;
            }

            if (row % 2 == 0)
            {
                ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = ZebraBg;
            }

            row++;
        }

        // Totals row
        if (days.Count > 0)
        {
            ws.Cell(row, 1).Value = "Total";
            ws.Cell(row, 2).Value = $"{days.Count} days";
            for (var c = 3; c <= 12; c++)
            {
                var colLetter = ws.Column(c).ColumnLetter();
                ws.Cell(row, c).FormulaA1 = $"=SUM({colLetter}2:{colLetter}{row - 1})";
                ws.Cell(row, c).Style.NumberFormat.Format = CurrencyFormat;
            }

            var totalRange = ws.Range(row, 1, row, 12);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = TotalBg;
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
        }

        ws.Columns(1, 12).AdjustToContents();
    }

    private static void BuildSuppliersSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var ws = workbook.Worksheets.Add("Suppliers");
        ws.ShowGridLines = true;

        string[] headers = ["Supplier Name", "Stock Received (tStok In)", "Payments Paid (supPayment)", "Current Balance / Due"];

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = BrandDark;
            cell.Style.Alignment.Horizontal = col == 0 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
        }

        var suppliers = totals.Monthly?.Suppliers ?? [];
        var row = 2;

        foreach (var sup in suppliers)
        {
            ws.Cell(row, 1).Value = sup.SupplierName;
            ws.Cell(row, 2).Value = sup.StockReceived;
            ws.Cell(row, 3).Value = sup.PaymentsMade;
            ws.Cell(row, 4).Value = sup.CurrentDue;

            for (var c = 2; c <= 4; c++)
            {
                ws.Cell(row, c).Style.NumberFormat.Format = CurrencyFormat;
            }

            if (row % 2 == 0)
            {
                ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = ZebraBg;
            }

            row++;
        }

        if (suppliers.Count > 0)
        {
            ws.Cell(row, 1).Value = "Total";
            for (var c = 2; c <= 4; c++)
            {
                var colLetter = ws.Column(c).ColumnLetter();
                ws.Cell(row, c).FormulaA1 = $"=SUM({colLetter}2:{colLetter}{row - 1})";
                ws.Cell(row, c).Style.NumberFormat.Format = CurrencyFormat;
            }

            var totalRange = ws.Range(row, 1, row, 4);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = TotalBg;
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
        }

        ws.Columns(1, 4).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 32);
    }

    private static void BuildExpensesPayrollSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var ws = workbook.Worksheets.Add("Expenses & Payroll");
        ws.ShowGridLines = true;

        // Table 1: Expenses by category
        ws.Cell("A1").Value = "Expense Category";
        ws.Cell("B1").Value = "Amount Spent";
        ws.Range("A1:B1").Style.Font.Bold = true;
        ws.Range("A1:B1").Style.Font.FontColor = XLColor.White;
        ws.Range("A1:B1").Style.Fill.BackgroundColor = HeaderBg;
        ws.Cell("B1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var categories = totals.Monthly?.Categories ?? [];
        var r = 2;
        foreach (var cat in categories)
        {
            ws.Cell(r, 1).Value = cat.Name ?? (cat.Category == ExpenseCategory.Refreshments ? "Snacks / refreshments" : cat.Category.ToString());
            ws.Cell(r, 2).Value = cat.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;
            r++;
        }

        if (categories.Count > 0)
        {
            ws.Cell(r, 1).Value = "Total expenses";
            ws.Cell(r, 2).FormulaA1 = $"=SUM(B2:B{r - 1})";
            ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;
            ws.Range(r, 1, r, 2).Style.Font.Bold = true;
            ws.Range(r, 1, r, 2).Style.Fill.BackgroundColor = TotalBg;
            ws.Range(r, 1, r, 2).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            r += 2;
        }

        // Table 2: Payroll taken by employee
        ws.Cell(r, 1).Value = "Employee Name";
        ws.Cell(r, 2).Value = "Monthly Salary";
        ws.Cell(r, 3).Value = "Salary Paid";
        ws.Cell(r, 4).Value = "Bonus Paid";
        ws.Cell(r, 5).Value = "Advance Paid";
        ws.Cell(r, 6).Value = "Total Taken";

        var payrollHeader = ws.Range(r, 1, r, 6);
        payrollHeader.Style.Font.Bold = true;
        payrollHeader.Style.Font.FontColor = XLColor.White;
        payrollHeader.Style.Fill.BackgroundColor = BrandBlue;

        for (var c = 2; c <= 6; c++)
        {
            ws.Cell(r, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        var startPayroll = r + 1;
        r++;

        var employees = totals.Monthly?.Employees ?? [];
        foreach (var emp in employees)
        {
            ws.Cell(r, 1).Value = emp.Employee;
            ws.Cell(r, 2).Value = emp.Salary ?? 0;
            ws.Cell(r, 3).Value = emp.SalaryPaid;
            ws.Cell(r, 4).Value = emp.BonusPaid;
            ws.Cell(r, 5).Value = emp.AdvancePaid;
            ws.Cell(r, 6).Value = emp.TotalPaid;

            for (var c = 2; c <= 6; c++)
            {
                ws.Cell(r, c).Style.NumberFormat.Format = CurrencyFormat;
            }
            r++;
        }

        if (employees.Count > 0)
        {
            ws.Cell(r, 1).Value = "Total payroll";
            for (var c = 2; c <= 6; c++)
            {
                var colLetter = ws.Column(c).ColumnLetter();
                ws.Cell(r, c).FormulaA1 = $"=SUM({colLetter}{startPayroll}:{colLetter}{r - 1})";
                ws.Cell(r, c).Style.NumberFormat.Format = CurrencyFormat;
            }
            var empTotal = ws.Range(r, 1, r, 6);
            empTotal.Style.Font.Bold = true;
            empTotal.Style.Fill.BackgroundColor = TotalBg;
            empTotal.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        ws.Columns(1, 6).AdjustToContents();
    }

    private static void BuildAssetsSheet(XLWorkbook workbook, ClosingTotals totals, string currency)
    {
        var ws = workbook.Worksheets.Add("Balance Sheet");
        ws.ShowGridLines = true;

        ws.Cell("A1").Value = "Asset / Liability";
        ws.Cell("B1").Value = "Opening Position";
        ws.Cell("C1").Value = "Closing Position";
        ws.Cell("D1").Value = "Net Change";

        var header = ws.Range("A1:D1");
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = BrandDark;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        var monthly = totals.Monthly;
        if (monthly is null)
        {
            ws.Columns(1, 4).AdjustToContents();
            return;
        }

        (string Label, decimal Opening, decimal Closing)[] rows =
        [
            ("Inventory / Stock Value", monthly.OpeningAssets.Stock, monthly.ClosingAssets.Stock),
            ("Physical Cash in Drawers", monthly.OpeningAssets.Cash, monthly.ClosingAssets.Cash),
            ("Bank / Digital Funds (MAIN)", monthly.OpeningAssets.Bank, monthly.ClosingAssets.Bank),
            ("Customer Due / Credit (Receivable)", monthly.OpeningAssets.CustomerDue, monthly.ClosingAssets.CustomerDue),
            ("Supplier Due (Payable - Subtract)", -monthly.OpeningAssets.SupplierDue, -monthly.ClosingAssets.SupplierDue),
            ("Lender Debt (Subtract)", -monthly.OpeningAssets.Debt, -monthly.ClosingAssets.Debt)
        ];

        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Label;
            ws.Cell(r, 2).Value = row.Opening;
            ws.Cell(r, 3).Value = row.Closing;
            ws.Cell(r, 4).FormulaA1 = $"=C{r}-B{r}";

            for (var c = 2; c <= 4; c++)
            {
                ws.Cell(r, c).Style.NumberFormat.Format = CurrencyFormat;
            }
            r++;
        }

        // Net assets row
        ws.Cell(r, 1).Value = "Net Assets";
        ws.Cell(r, 2).FormulaA1 = $"=SUM(B2:B{r - 1})";
        ws.Cell(r, 3).FormulaA1 = $"=SUM(C2:C{r - 1})";
        ws.Cell(r, 4).FormulaA1 = $"=C{r}-B{r}";

        for (var c = 2; c <= 4; c++)
        {
            ws.Cell(r, c).Style.NumberFormat.Format = CurrencyFormat;
        }

        var totalRange = ws.Range(r, 1, r, 4);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = TotalBg;
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

        r += 2;
        ws.Cell(r, 1).Value = "Assets Built This Month";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 2).FormulaA1 = $"=D{r - 2}";
        ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 2).Style.Font.FontSize = 12;
        ws.Cell(r, 2).Style.NumberFormat.Format = CurrencyFormat;
        ws.Cell(r, 2).Style.Font.FontColor = monthly.AssetGrowth >= 0 ? SuccessGreen : DangerRed;

        ws.Columns(1, 4).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 38);
    }
}

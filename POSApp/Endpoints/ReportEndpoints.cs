using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using POSApp.Services;

namespace POSApp.Endpoints;

public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.MakeSales);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/export/{kind}", ExportAsync);
        return group;
    }

    public static async Task<IResult> ExportAsync(string kind, string? period, DateOnly? start, DateOnly? end,
        int? locationId, int? supplierId, string? search, string? sort, HttpContext context, ReportService reports,
        ReceivingReportService receiving, InventoryService inventory, SupplierService suppliers,
        InventoryValuation valuation,
        string[]? columns = null)
    {
        const int limit = 10000;
        var rows = new List<string[]>();
        ReportRange? range = null;
        int count;
        if (kind == "products")
        {
            var result = await inventory.ListAsync(context.User, search, ct: context.RequestAborted,
                locationId: locationId, pageSize: limit + 1, sort: "id");
            count = await inventory.CountAsync(context.User, search, context.RequestAborted);
            rows.Add(["Item ID", "Item", "Category", "Supplier", "Barcode", "Unit", "Stock", "Selling price", "Average cost", "Stock value"]);
            rows.AddRange(result.Select(item => new[] { Number(item.Id), item.Name, item.Category ?? "", item.Supplier,
                item.Barcode ?? "", item.Unit, Number(item.Stock), Number(item.Price), item.Cost is { } cost ? Number(cost) : "",
                item.Cost is { } unitCost ? Number(item.Stock * unitCost) : "" }));
            rows.Add(["TOTAL", "", "", "", "", "", "", "", "", ""]);
        }
        else if (kind == "suppliers")
        {
            var result = await suppliers.ListAsync(context.User, search, context.RequestAborted);
            count = result.Count;
            rows.Add(["Supplier ID", "Supplier", "Phone", "Opening balance", "Current balance", "Assigned stock value"]);
            rows.AddRange(result.Take(limit).Select(supplier => new[] { Number(supplier.Id), supplier.Name, supplier.Phone ?? "",
                Number(supplier.OpeningBalance), Number(supplier.Balance), Number(supplier.StockValue) }));
        }
        else if (kind == "inventory")
        {
            var data = await valuation.ReadAsync(context.User, supplierId, locationId, search, 1,
                context.RequestAborted, sort, limit + 1);
            count = data.Count;
            rows.Add(["Item ID", "Item", "Barcode", "Default supplier", "Quantity", "Average cost", "Stock value"]);
            rows.AddRange(data.Rows.Select(item => new[] { Number(item.Id), item.Name, item.Barcode ?? "", item.Supplier,
                Number(item.Quantity), Number(item.UnitCost), Number(item.Value) }));
            rows.Add(["TOTAL", "", "", "", "", "", Number(data.TotalValue)]);
        }
        else if (kind == "sales")
        {
            var data = await reports.ReadAsync(context.User, true, period, start, end,
                ct: context.RequestAborted, search: search, sort: sort, locationId: locationId, pageSize: limit);
            range = data.Range;
            count = data.Count;
            rows.Add(data.CanViewCosts
                ? ["Sale ID", "Receipt", "Customer", "Date (UTC)", "Sales", "Paid", "Due", "Cost", "Profit"]
                : ["Sale ID", "Receipt", "Customer", "Date (UTC)", "Sales", "Paid", "Due"]);
            foreach (var row in data.Rows)
            {
                string[] values = [Number(row.Id), row.Number, row.Party, row.At.ToString("O"), Number(row.Total), Number(row.Paid), Number(row.Total - row.Paid)];
                rows.Add(data.CanViewCosts ? [.. values, Number(row.Cost ?? 0), Number(row.Profit ?? 0)] : values);
            }
            string[] totals = ["TOTAL", "", "", "", Number(data.Total), Number(data.Paid), Number(data.Total - data.Paid)];
            rows.Add(data.CanViewCosts ? [.. totals, Number(data.Cost ?? 0), Number(data.Profit ?? 0)] : totals);
        }
        else if (kind == "items")
        {
            var data = await reports.ProductsAsync(context.User, period, start, end,
                ct: context.RequestAborted, search: search, sort: sort, locationId: locationId, pageSize: limit);
            range = data.Range;
            count = data.Count;
            rows.Add(["Item ID", "Item", "Barcode", "Unit", "Net quantity", "Average cost", "Cost", "Subtotal", "Discount", "Sales", "Profit"]);
            rows.AddRange(data.Rows.Select(row => new[] { Number(row.ProductId), row.Name, row.Barcode, row.Unit,
                Number(row.Quantity), row.AverageCost is { } cost ? Number(cost) : "", Number(row.Cost), Number(row.Subtotal),
                Number(row.Discount), Number(row.Total), Number(row.Profit) }));
            rows.Add(["TOTAL", "", "", "", "", "", Number(data.Cost), Number(data.Subtotal), Number(data.Discount), Number(data.Total), Number(data.Profit)]);
        }
        else if (kind == "receiving")
        {
            var data = await receiving.ReadAsync(context.User, period, start, end,
                locationId, ct: context.RequestAborted, search: search, sort: sort, pageSize: limit);
            range = data.Range;
            count = data.Count;
            rows.Add(["ID", "Type", "Date (UTC)", "Item", "Barcode", "From", "To", "Quantity", "Unit cost", "Value"]);
            foreach (var row in data.Rows)
            {
                if (row.Items.Count > 0)
                {
                    foreach (var item in row.Items)
                    {
                        rows.Add([Number(row.Id), row.IsTransfer ? "Transfer" : "Purchase",
                            row.At.ToString("O"), item.Product, item.Barcode ?? "", row.Source, row.Destination,
                            Number(item.Quantity), Number(item.UnitCost), Number(item.Value)]);
                    }
                }
                else
                {
                    rows.Add([Number(row.Id), row.IsTransfer ? "Transfer" : "Purchase",
                        row.At.ToString("O"), row.Product, row.Barcode ?? "", row.Source, row.Destination,
                        Number(row.Quantity), Number(row.UnitCost), Number(row.Value)]);
                }
            }
            rows.Add(["TOTAL", "", "", "", "", "", "", "", "", Number(data.NetValue)]);
        }
        else if (kind == "stock-loss")
        {
            var data = await reports.StockLossReportAsync(context.User, period, start, end,
                locationId, search: search, page: 1, pageSize: limit, ct: context.RequestAborted);
            range = data.Range;
            count = data.Rows.Count;
            rows.Add(["Date (UTC)", "Location", "Item ID", "Item", "Barcode", "Unit", "Reason", "Quantity Lost", "Unit Cost", "Total Financial Loss", "Notes"]);
            rows.AddRange(data.Rows.Select(row => new[] {
                row.OccurredAt.ToString("O"), row.LocationName, Number(row.ProductId), row.ProductName,
                row.Barcode ?? "", row.Unit, row.Reason, Number(row.Quantity), Number(row.UnitCost),
                Number(row.TotalLoss), row.Notes ?? ""
            }));
            rows.Add(["TOTAL", "", "", "", "", "", "", Number(data.TotalQuantity), "", Number(data.TotalFinancialLoss), ""]);
        }
        else if (kind == "categories")
        {
            var analytics = context.RequestServices.GetRequiredService<AnalyticsService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var zone = AccountingCalendar.Zone(config);
            range = ReportService.ResolveRange(period, start, end, zone, DateTimeOffset.UtcNow);
            var data = await analytics.GetCategoryPerformanceAsync(context.User, range.Start, range.End, locationId, context.RequestAborted);
            count = data.Categories.Count;
            rows.Add(["Category ID", "Category Name", "Products Sold", "Units Sold", "Total Revenue", "Total Cost", "Gross Profit", "Margin %", "Revenue Share %", "Top Product"]);
            rows.AddRange(data.Categories.Select(c => new[] {
                Number(c.CategoryId),
                c.CategoryName,
                Number(c.ProductCount),
                Number(c.UnitsSold),
                Number(c.TotalRevenue),
                Number(c.TotalCost),
                Number(c.GrossProfit),
                Number(c.MarginPercent),
                Number(c.RevenueSharePercent),
                c.TopProductName
            }));
            rows.Add(["TOTAL", $"{data.Categories.Count} categories",
                Number(data.Categories.Sum(c => c.ProductCount)),
                Number(data.Categories.Sum(c => c.UnitsSold)),
                Number(data.TotalRevenue),
                Number(data.TotalCost),
                Number(data.TotalProfit),
                Number(data.OverallMarginPercent),
                "100.0",
                ""]);
        }
        else if (kind == "suppliers-sales")
        {
            var analytics = context.RequestServices.GetRequiredService<AnalyticsService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var zone = AccountingCalendar.Zone(config);
            range = ReportService.ResolveRange(period, start, end, zone, DateTimeOffset.UtcNow);
            var data = await analytics.GetSupplierSalesPerformanceAsync(context.User, range.Start, range.End, locationId, context.RequestAborted);
            count = data.Suppliers.Count;
            rows.Add(["Supplier ID", "Supplier Name", "Distinct Items Sold", "Units Sold", "Total Revenue", "Total Cost", "Gross Profit", "Margin %", "Stock Units", "Stock Value"]);
            rows.AddRange(data.Suppliers.Select(s => new[] {
                Number(s.SupplierId),
                s.SupplierName,
                Number(s.ProductsSoldDistinct),
                Number(s.UnitsSold),
                Number(s.TotalRevenue),
                Number(s.TotalCost),
                Number(s.GrossProfit),
                Number(s.MarginPercent),
                Number(s.CurrentStockUnits),
                Number(s.CurrentStockValue)
            }));
            rows.Add(["TOTAL", $"{data.Suppliers.Count} suppliers",
                Number(data.Suppliers.Sum(s => s.ProductsSoldDistinct)),
                Number(data.Suppliers.Sum(s => s.UnitsSold)),
                Number(data.TotalRevenue),
                Number(data.Suppliers.Sum(s => s.TotalCost)),
                Number(data.TotalProfit),
                data.TotalRevenue > 0 ? Number(decimal.Round(data.TotalProfit / data.TotalRevenue * 100, 1)) : "0",
                Number(data.Suppliers.Sum(s => s.CurrentStockUnits)),
                Number(data.TotalStockValue)]);
        }
        else if (kind == "inventory-velocity")
        {
            var analytics = context.RequestServices.GetRequiredService<AnalyticsService>();
            int threshold = int.TryParse(context.Request.Query["daysThreshold"], out var t) ? t : 60;
            int? catId = int.TryParse(context.Request.Query["categoryId"], out var cid) ? cid : null;
            var data = await analytics.GetInventoryVelocityAsync(context.User, threshold, locationId, catId, context.RequestAborted);
            count = data.Items.Count;
            rows.Add(["Product ID", "Product Name", "Barcode", "Category", "Supplier", "Stock Qty", "Cost Price", "Selling Price", "Tied-Up Capital", "Last Sold Date", "Days Since Last Sale", "Units Sold in Period", "Velocity Tier"]);
            rows.AddRange(data.Items.Select(item => new[] {
                Number(item.ProductId),
                item.ProductName,
                item.Barcode ?? "",
                item.CategoryName,
                item.SupplierName,
                Number(item.QuantityOnHand),
                Number(item.CostPrice),
                Number(item.SellingPrice),
                Number(item.TiedUpCapital),
                item.LastSoldDate?.ToString("yyyy-MM-dd") ?? "Never",
                Number(item.DaysSinceLastSale),
                Number(item.UnitsSoldPeriod),
                item.Tier.ToString()
            }));
            rows.Add(["TOTAL", $"{data.Items.Count} products", "", "", "",
                Number(data.Items.Sum(i => i.QuantityOnHand)),
                "", "",
                Number(data.Items.Sum(i => i.TiedUpCapital)),
                "", "", "",
                $"Dead: {Number(data.TotalDeadStockCapital)}"]);
        }
        else if (kind == "margin-matrix")
        {
            var analytics = context.RequestServices.GetRequiredService<AnalyticsService>();
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var zone = AccountingCalendar.Zone(config);
            range = ReportService.ResolveRange(period, start, end, zone, DateTimeOffset.UtcNow);
            var data = await analytics.GetMarginMatrixAsync(context.User, range.Start, range.End, locationId, context.RequestAborted);
            var quadrant = search;
            var filtered = string.IsNullOrEmpty(quadrant) ? data.Items : data.Items.Where(i => i.Quadrant.ToString().Equals(quadrant, StringComparison.OrdinalIgnoreCase)).ToList();
            count = filtered.Count;
            rows.Add(["Product ID", "Product Name", "Category", "Units Sold", "Total Revenue", "Total Cost", "Gross Profit", "Margin %", "Quadrant"]);
            rows.AddRange(filtered.Select(item => new[] {
                Number(item.ProductId),
                item.ProductName,
                item.CategoryName,
                Number(item.UnitsSold),
                Number(item.TotalRevenue),
                Number(item.TotalCost),
                Number(item.TotalProfit),
                Number(item.MarginPercent),
                item.Quadrant.ToString()
            }));
            rows.Add(["TOTAL", $"{filtered.Count} products", "",
                Number(filtered.Sum(i => i.UnitsSold)),
                Number(filtered.Sum(i => i.TotalRevenue)),
                Number(filtered.Sum(i => i.TotalCost)),
                Number(filtered.Sum(i => i.TotalProfit)),
                filtered.Sum(i => i.TotalRevenue) > 0 ? Number(decimal.Round(filtered.Sum(i => i.TotalProfit) / filtered.Sum(i => i.TotalRevenue) * 100, 1)) : "0",
                ""]);
        }
        else
        {
            return Results.NotFound();
        }
        if (count > limit)
        {
            throw new ValidationException("This export exceeds 10,000 rows. Choose a shorter date range or a more specific search.");
        }
        if (columns is { Length: > 0 } && rows.Count > 0)
        {
            var selected = columns.ToHashSet(StringComparer.Ordinal);
            var indexes = rows[0].Select((header, index) => (header, index))
                .Where(column => selected.Contains(column.header))
                .Select(column => column.index)
                .ToArray();
            if (indexes.Length > 0)
            {
                rows = rows.Select(row => indexes
                    .Where(index => index < row.Length)
                    .Select(index => row[index])
                    .ToArray()).ToList();
            }
        }
        context.Response.Headers.CacheControl = "no-store";
        var csv = string.Join("\r\n", rows.Select(row => string.Join(',', row.Select(Escape))));
        var fileDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var fileName = range is null
            ? $"pos-{kind}-{fileDate}.csv"
            : $"pos-{kind}-{range.Start:yyyy-MM-dd}-{range.End:yyyy-MM-dd}.csv";
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
            "text/csv; charset=utf-8", fileName);
    }

    private static string Number(decimal value) => value.ToString("0.####################", CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        // Prevent names/notes being interpreted as spreadsheet formulas.
        if (value.Length > 0 && "=+@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }
        if (value.StartsWith('-') && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public static class StockAdjustmentJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static IReadOnlyList<StockAdjustmentEntry> Parse(string text)
    {
        StockAdjustmentImportRow[]? rows;
        try
        {
            rows = JsonSerializer.Deserialize<StockAdjustmentImportRow[]>(text.TrimStart('\uFEFF'), Options);
        }
        catch (JsonException)
        {
            throw new ValidationException("Invalid JSON. Use an array of objects, each with productId, quantity and unitCost. All three fields are required.");
        }
        if (rows is null || rows.Length == 0)
        {
            throw new ValidationException("The file contains no stock rows. Add at least one product.");
        }
        if (rows.Length > 10000)
        {
            throw new ValidationException($"The file contains {rows.Length:N0} rows. Import at most 10,000 products per file.");
        }
        var firstRows = new Dictionary<int, int>();
        for (var index = 0; index < rows.Length; index++)
        {
            var rowNumber = index + 1;
            var row = rows[index];
            if (row is null)
            {
                throw new ValidationException($"Row {rowNumber}: expected a product object, found null.");
            }
            if (row.ProductId <= 0)
            {
                throw new ValidationException($"Row {rowNumber}: productId must be an existing product ID greater than zero (received {row.ProductId}). Replace the example ID with the ID from Items.");
            }
            if (row.Quantity is < -999999999999999.999m or > 999999999999999.999m
                || decimal.Round(row.Quantity, 3) != row.Quantity)
            {
                throw new ValidationException($"Row {rowNumber} (product #{row.ProductId}): quantity must be between -999,999,999,999,999.999 and 999,999,999,999,999.999 with up to three decimal places (received {row.Quantity.ToString(CultureInfo.InvariantCulture)}).");
            }
            if (row.UnitCost < 0)
            {
                throw new ValidationException($"Row {rowNumber} (product #{row.ProductId}): unitCost cannot be negative (received {row.UnitCost.ToString(CultureInfo.InvariantCulture)}).");
            }
            if (!firstRows.TryAdd(row.ProductId, rowNumber))
            {
                throw new ValidationException($"Row {rowNumber}: product #{row.ProductId} also appears on row {firstRows[row.ProductId]}. Combine its quantities into one row with the appropriate unit cost.");
            }
        }
        return rows.Select(row => new StockAdjustmentEntry(row.ProductId, row.Quantity, row.UnitCost)).ToArray();
    }

    public static Guid RequestId(string owner, int locationId, DateOnly date, string notes,
        IReadOnlyList<StockAdjustmentEntry> items)
    {
        // Canonical data makes reselecting the same file after a lost response safe.
        var content = JsonSerializer.Serialize(new
        {
            Owner = owner,
            LocationId = locationId,
            Date = date,
            Notes = notes.Trim(),
            Items = items.OrderBy(item => item.ProductId).Select(item => new
            {
                item.ProductId,
                Quantity = item.Quantity.ToString("G29", CultureInfo.InvariantCulture),
                UnitCost = item.UnitCost.ToString("G29", CultureInfo.InvariantCulture)
            })
        }, Options);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return new Guid(hash.AsSpan(0, 16));
    }

}

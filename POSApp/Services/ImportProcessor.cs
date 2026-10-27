using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.Services;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class ImportProcessor(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<string> ProcessAsync(Guid id, ClaimsPrincipal actor, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ImportJobs.AsNoTracking().SingleAsync(job => job.Id == id, ct);
        var input = JsonSerializer.Deserialize<ImportInput>(job.Payload, ImportService.JsonOptions)
            ?? throw new ValidationException("The uploaded file is empty.");
        if (job.Kind == "suppliers")
        {
            return await SuppliersAsync(id, actor, input, ct);
        }
        if (job.Kind == "products")
        {
            return await ProductsAsync(id, actor, input, ct);
        }
        var entries = StockAdjustmentJson.Parse(input.Json);
        if (input.LocationId is not { } locationId || input.Date is not { } date)
        {
            throw new ValidationException("Select a location and adjustment date before uploading.");
        }
        // Retrying an interrupted job reuses the existing posting identity.
        var requestId = StockAdjustmentJson.RequestId(job.UserId.ToString(), locationId, date, input.Notes, entries);
        var result = await scope.ServiceProvider.GetRequiredService<StockAdjustmentService>()
            .CreateAsync(actor, new(requestId, locationId, date, input.Notes, entries), ct);
        return $"Added {result.ItemCount:N0} products. Stock adjustment #{result.Id}.";
    }

    private async Task<string> ProductsAsync(Guid jobId, ClaimsPrincipal actor, ImportInput input, CancellationToken ct)
    {
        ProductImportRow[] rows;
        try
        {
            rows = JsonSerializer.Deserialize<ProductImportRow[]>(input.Json.TrimStart('\uFEFF'), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                MaxDepth = 12,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? [];
        }
        catch (JsonException)
        {
            throw new ValidationException("Invalid product JSON. Include name, category name and supplier name for each product.");
        }
        if (rows.Length is < 1 or > 10000)
        {
            throw new ValidationException("Include between 1 and 10,000 products.");
        }

        var rowErrors = ValidateProductRows(rows);
        var duplicateBarcodes = rows.Select((row, index) => (Row: row, RowNumber: index + 1))
            .Where(item => item.Row is not null && !string.IsNullOrWhiteSpace(item.Row.Barcode))
            .GroupBy(item => item.Row.Barcode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"barcode '{group.Key}' is repeated on rows {string.Join(", ", group.Select(item => $"{item.RowNumber} (ID {item.Row.Id?.ToString() ?? "new"})"))}.")
            .ToArray();
        if (rowErrors.Count > 0 || duplicateBarcodes.Length > 0)
        {
            throw new ValidationException($"Fix these product rows: {string.Join(" ", rowErrors.Concat(duplicateBarcodes))}");
        }
        var providedIds = rows.Where(row => row.Id.HasValue).Select(row => row.Id!.Value).ToArray();

        var barcodes = rows.Where(row => !string.IsNullOrWhiteSpace(row.Barcode)).Select(row => row.Barcode!.Trim()).ToArray();
        return await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var receipt = await db.RecordChanges.SingleOrDefaultAsync(change => change.RequestId == jobId
                && change.RecordType == "productImport", ct);
            if (receipt is not null)
            {
                return receipt.Reason;
            }
            if (providedIds.Length > 0 && await db.Products.AnyAsync(product => providedIds.Contains(product.Id), ct))
            {
                throw new ValidationException("One or more product IDs already exist. No products were imported.");
            }

            var requestedSupplierNames = rows.Select(row => NormalizeSupplierName(row.Supplier))
                .Distinct(StringComparer.Ordinal).ToArray();
            var supplierByName = (await db.Suppliers.Where(supplier => !supplier.IsDeleted
                    && requestedSupplierNames.Contains(supplier.NormalizedName))
                .ToListAsync(ct))
                .GroupBy(supplier => supplier.NormalizedName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var (row, rowIndex) in rows.Select((row, index) => (row, index)))
            {
                if (!supplierByName.ContainsKey(NormalizeSupplierName(row.Supplier)))
                {
                    throw new ValidationException($"Supplier '{row.Supplier.Trim()}' for product row {rowIndex + 1} (ID {row.Id?.ToString() ?? "new"}) was not found. Import suppliers first or correct the name.");
                }
            }

            var requestedCategories = rows.Select(row => row.Category.Trim())
                .GroupBy(NameNormalization.Normalize, StringComparer.Ordinal)
                .Select(group => (NormalizedName: group.Key, Name: group.First()))
                .ToArray();
            var requestedCategoryNames = requestedCategories.Select(category => category.NormalizedName).ToArray();
            var existingCategories = await db.Categories
                .Where(category => requestedCategoryNames.Contains(category.NormalizedName)).ToListAsync(ct);
            var categoryByName = existingCategories
                .GroupBy(category => category.NormalizedName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(category => category.IsDeleted).First(), StringComparer.Ordinal);
            foreach (var requestedCategory in requestedCategories)
            {
                if (categoryByName.TryGetValue(requestedCategory.NormalizedName, out var existingCategory))
                {
                    existingCategory.IsDeleted = false;
                    existingCategory.DeletedAt = null;
                }
                else
                {
                    var category = new Category
                    {
                        Name = requestedCategory.Name,
                        NormalizedName = requestedCategory.NormalizedName,
                        CreatedByUserId = actorId
                    };
                    db.Categories.Add(category);
                    categoryByName[requestedCategory.NormalizedName] = category;
                }
            }

            var allAttributeKeys = rows.SelectMany(row => row.Attributes is null
                    ? Enumerable.Empty<string>()
                    : row.Attributes.Keys)
                .Select(key => key.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var definitions = await db.ProductAttributeDefinitions.Include(definition => definition.Options)
                .Where(definition => !definition.IsDeleted && allAttributeKeys.Contains(definition.Key.ToLower()))
                .ToDictionaryAsync(definition => definition.Key, StringComparer.OrdinalIgnoreCase, ct);
            foreach (var key in allAttributeKeys)
            {
                if (!definitions.ContainsKey(key))
                {
                    throw new ValidationException($"Product attribute '{key}' is not configured. Add it under Items → Attributes first.");
                }
            }
            var barcodeTerms = barcodes.ToArray();
            var normalizedBarcodes = barcodeTerms.Select(barcode => barcode.ToLowerInvariant()).ToArray();
            if (barcodeTerms.Length > 0 && await db.Products.AnyAsync(product => product.Barcode != null
                && normalizedBarcodes.Contains(product.Barcode.ToLower()), ct))
            {
                throw new ValidationException("One or more barcodes already belong to products. No products were imported.");
            }

            var products = new List<Product>(rows.Length);
            foreach (var row in rows)
            {
                var product = new Product
                {
                    Id = row.Id ?? 0,
                    Name = row.Name.Trim(),
                    NormalizedName = NameNormalization.Normalize(row.Name),
                    Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(),
                    Unit = row.Unit.Trim(),
                    CostPrice = row.IsNonStocked ? 0 : row.CostPrice,
                    SellingPrice = row.SellingPrice,
                    Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                    Category = categoryByName[NameNormalization.Normalize(row.Category)],
                    Supplier = supplierByName[NormalizeSupplierName(row.Supplier)],
                    AllowFractionUnit = row.AllowFractionUnit,
                    IsNonStocked = row.IsNonStocked,
                    CreatedByUserId = actorId
                };
                if (product.Barcode is { } barcode && barcode.Any(character => character < ' ' || character > '~'))
                {
                    throw new ValidationException($"Product '{product.Name}' has a barcode containing unsupported characters.");
                }
                if (row.IsNonStocked && row.CostPrice != 0)
                {
                    throw new ValidationException($"Non-stocked product '{product.Name}' must have a zero cost price.");
                }
                foreach (var (rawKey, jsonValue) in row.Attributes ?? [])
                {
                    var key = rawKey.Trim().ToLowerInvariant();
                    var definition = definitions[key];
                    var (value, displayValue) = ParseAttributeValue(definition, jsonValue, product.Name);
                    if (value is not null)
                    {
                        product.AttributeValues.Add(new ProductAttributeValue
                        {
                            Definition = definition,
                            Value = value,
                            DisplayValue = displayValue
                        });
                    }
                }
                products.Add(product);
            }
            if (providedIds.Length > 0 && db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Products\" IN SHARE ROW EXCLUSIVE MODE", ct);
            }
            db.Products.AddRange(products);
            foreach (var product in products)
            {
                db.ProductCostHistories.Add(new ProductCostHistory
                {
                    Product = product,
                    OldCostPrice = 0,
                    NewCostPrice = product.CostPrice,
                    Reason = "Initial cost price (Import)",
                    ReferenceType = "Import",
                    CreatedByUserId = actorId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync(ct);
            if (providedIds.Length > 0 && db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                await db.Database.ExecuteSqlRawAsync("""
                    SELECT setval(
                        pg_get_serial_sequence('"Products"', 'Id'),
                        COALESCE((SELECT MAX("Id") FROM "Products"), 1),
                        EXISTS (SELECT 1 FROM "Products"));
                    """, ct);
            }
            foreach (var product in products.Where(product => product.Barcode is null))
            {
                product.Barcode = BarcodeService.FromProductId(product.Id);
            }
            await db.SaveChangesAsync(ct);
            var result = $"Imported {products.Count:N0} products. Stock quantities and supplier balances were not changed.";
            db.RecordChanges.Add(new RecordChange
            {
                RequestId = jobId,
                RecordType = "productImport",
                RecordId = 0,
                Action = "Create",
                Reason = result,
                Before = "{}",
                After = JsonSerializer.Serialize(new { Count = products.Count }),
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, ct);
    }

    private static IReadOnlyList<string> ValidateProductRows(ProductImportRow[] rows)
    {
        var errors = new List<string>();

        for (var index = 0; index < rows.Length && errors.Count < 10; index++)
        {
            var row = rows[index];
            var prefix = $"Row {index + 1} (ID {row?.Id?.ToString() ?? "new"})";
            if (row is null)
            {
                errors.Add($"{prefix} must be a JSON object.");
                continue;
            }

            if (row.Id is <= 0)
            {
                errors.Add($"{prefix} has an invalid ID. Omit ID to let the database generate one, or provide a positive unused ID.");
            }

            AddTextError(row.Name, 200, "name");
            AddTextError(row.Category, 120, "category");
            AddTextError(row.Supplier, 200, "supplier");
            AddTextError(row.Unit, 32, "unit");

            if (row.Barcode?.Length > 64)
            {
                errors.Add($"{prefix} barcode exceeds 64 characters.");
            }
            if (row.Description?.Length > 2000)
            {
                errors.Add($"{prefix} description exceeds 2,000 characters.");
            }
            if (row.CostPrice < 0 || row.CostPrice > 9999999999999999.99m
                || decimal.Round(row.CostPrice, 2) != row.CostPrice)
            {
                errors.Add($"{prefix} has an invalid cost price.");
            }
            if (row.SellingPrice < 0 || row.SellingPrice > 9999999999999999.99m
                || decimal.Round(row.SellingPrice, 2) != row.SellingPrice)
            {
                errors.Add($"{prefix} has an invalid selling price.");
            }
            if (row.Attributes?.Count > 100)
            {
                errors.Add($"{prefix} has more than 100 attributes.");
            }

            void AddTextError(string? value, int maxLength, string field)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add($"{prefix} is missing {field}.");
                }
                else if (value.Trim().Length > maxLength)
                {
                    errors.Add($"{prefix} {field} exceeds {maxLength} characters.");
                }
            }
        }

        if (errors.Count == 10)
        {
            errors.Add("Additional row errors were omitted.");
        }

        var duplicateIds = rows.Select((row, index) => (row?.Id, RowNumber: index + 1))
            .Where(item => item.Id.HasValue)
            .GroupBy(item => item.Id!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => $"product ID {group.Key} is repeated on rows {string.Join(" and ", group.Select(item => item.RowNumber))}.")
            .ToArray();
        errors.AddRange(duplicateIds);

        return errors;
    }

    private static string NormalizeSupplierName(string name) => NameNormalization.Normalize(name);

    private static (string? Value, string? DisplayValue) ParseAttributeValue(
        ProductAttributeDefinition definition,
        JsonElement value,
        string productName)
    {
        switch (definition.Kind)
        {
            case ProductAttributeKind.Checkbox when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return (value.GetBoolean() ? "true" : "false", null);
            case ProductAttributeKind.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number):
                if (definition.NonNegative && number < 0
                    || (!definition.AllowFraction && decimal.Truncate(number) != number))
                {
                    throw new ValidationException($"Product '{productName}' has an invalid value for attribute '{definition.Key}'.");
                }
                return (number.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
            case ProductAttributeKind.Select when value.ValueKind == JsonValueKind.String:
            case ProductAttributeKind.Text when value.ValueKind == JsonValueKind.String:
                var text = value.GetString()?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    return (null, null);
                }
                if (text.Length > 2000 || (definition.Kind == ProductAttributeKind.Select
                    && !definition.Options.Any(option => string.Equals(option.Value, text, StringComparison.OrdinalIgnoreCase))))
                {
                    throw new ValidationException($"Product '{productName}' has an invalid value for attribute '{definition.Key}'.");
                }
                if (definition.Kind == ProductAttributeKind.Select)
                {
                    var option = definition.Options.First(option => string.Equals(
                        option.Value,
                        text,
                        StringComparison.OrdinalIgnoreCase));
                    return (option.Value, option.DisplayName);
                }

                return (text, null);
            default:
                throw new ValidationException($"Product '{productName}' has an invalid JSON value for attribute '{definition.Key}'.");
        }
    }

    private async Task<string> SuppliersAsync(Guid jobId, ClaimsPrincipal actor, ImportInput input, CancellationToken ct)
    {
        SupplierImportRow[] rows;
        try
        {
            rows = JsonSerializer.Deserialize<SupplierImportRow[]>(input.Json.TrimStart('\uFEFF'), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                MaxDepth = 8,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? [];
        }
        catch (JsonException)
        {
            throw new ValidationException("Invalid supplier JSON. Use an array with a required name, optional id and phone, and an optional numeric balance.");
        }
        if (rows.Length is < 1 or > 10000 || rows.Any(row => row is null || row.Id is <= 0
            || string.IsNullOrWhiteSpace(row.Name) || row.Name.Trim().Length > 200 || row.Phone?.Length > 64
            || (row.Balance < -9999999999999999.99m || row.Balance > 9999999999999999.99m) || decimal.Round(row.Balance, 2) != row.Balance))
        {
            throw new ValidationException("Include 1-10,000 suppliers with names up to 200 characters, phones up to 64 characters and balances with at most two decimals. IDs are optional; supplied IDs must be positive and unique.");
        }
        var ids = rows.Where(row => row.Id.HasValue).Select(row => row.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new ValidationException("Supplied supplier IDs must be unique. Omit IDs to generate new supplier accounts.");
        }
        return await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var receipt = await db.RecordChanges.SingleOrDefaultAsync(change => change.RequestId == jobId
                && change.RecordType == "supplierImport", ct);
            if (receipt is not null)
            {
                return receipt.Reason;
            }
            if (ids.Length > 0 && await db.Suppliers.IgnoreQueryFilters()
                .AnyAsync(supplier => ids.Contains(supplier.LegacyId), ct))
            {
                throw new ValidationException("One or more old supplier IDs have already been imported. No suppliers were added.");
            }
            var date = input.Date ?? throw new ValidationException("The import has no accounting date. Upload the file again.");
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var occurredAt = AccountingCalendar.At(date, configuration);
            foreach (var row in rows)
            {
                var balance = new SupplierBalance
                {
                    SupplierId = 0,
                    OpeningBalance = row.Balance,
                    CurrentBalance = row.Balance,
                    OpenedAt = occurredAt,
                    CreatedByUserId = actorId,
                    Entries =
                    [
                        new SupplierBalanceEntry
                        {
                            SupplierBalanceId = 0,
                            Type = BalanceEntryType.OpeningBalance,
                            Amount = row.Balance,
                            BalanceAfter = row.Balance,
                            OccurredAt = occurredAt,
                            Notes = row.Id is { } sourceId ? $"Supplier migration · old ID {sourceId}" : "Supplier migration",
                            CreatedByUserId = actorId
                        }
                    ]
                };
                db.Suppliers.Add(new Supplier
                {
                    LegacyId = row.Id,
                    Name = row.Name.Trim(),
                    NormalizedName = NameNormalization.Normalize(row.Name),
                    Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
                    CreatedByUserId = actorId,
                    Balance = balance
                });
            }
            var result = $"Imported {rows.Length:N0} suppliers with opening balances.";
            db.RecordChanges.Add(new RecordChange
            {
                RequestId = jobId,
                RecordType = "supplierImport",
                RecordId = 0,
                Action = "Create",
                Reason = result,
                Before = "{}",
                After = JsonSerializer.Serialize(new { Count = rows.Length, LegacyIds = rows.Select(row => row.Id) }),
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, ct);
    }
}

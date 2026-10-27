using POSApp.Client.Enums;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Persistence.Entities;

namespace POSApp.Persistence;

public static class DevelopmentDataSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task SeedAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue("DevelopmentSeed:Enabled", true))
        {
            return;
        }

        var path = Path.Combine(environment.ContentRootPath, "Data", "development-seed.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "Data", "development-seed.json");
        }

        await using var file = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<SeedData>(file, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The development seed file is empty.");

        Validate(data);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var zone = POSApp.Services.AccountingCalendar.Zone(configuration);
        var openingAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            data.OpeningDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        await RepairOpeningDatesAsync(db, data, openingAt, cancellationToken);

        var categories = new Dictionary<string, Category>(StringComparer.Ordinal);
        foreach (var item in data.Categories)
        {
            var category = await db.Categories.SingleOrDefaultAsync(
                existing => existing.Name == item.Name,
                cancellationToken);

            if (category is null)
            {
                category = new Category
                {
                    Name = item.Name,
                    NormalizedName = NameNormalization.Normalize(item.Name),
                    Description = item.Description
                };
                db.Categories.Add(category);
            }

            categories.Add(item.Name, category);
        }

        var locations = new Dictionary<string, StockLocation>(StringComparer.Ordinal);
        foreach (var item in data.Locations)
        {
            var location = await db.StockLocations.SingleOrDefaultAsync(
                existing => existing.Code == item.Code,
                cancellationToken);

            if (location is null)
            {
                location = new StockLocation
                {
                    Code = item.Code,
                    Name = item.Name,
                    NormalizedName = NameNormalization.Normalize(item.Name)
                };
                db.StockLocations.Add(location);
            }

            locations.Add(item.Code, location);
        }

        var suppliers = new Dictionary<string, Supplier>(StringComparer.Ordinal);
        foreach (var item in data.Suppliers)
        {
            var existingSupplier = await db.Suppliers.SingleOrDefaultAsync(existing => existing.Name == item.Name, cancellationToken);
            if (existingSupplier is not null)
            {
                suppliers.Add(item.Name, existingSupplier);
                continue;
            }

            var supplier = new Supplier
            {
                Name = item.Name,
                NormalizedName = NameNormalization.Normalize(item.Name),
                ContactName = item.ContactName
            };
            suppliers.Add(item.Name, supplier);
            var balance = new SupplierBalance
            {
                SupplierId = 0,
                Supplier = supplier,
                OpeningBalance = item.OpeningBalance,
                CurrentBalance = item.OpeningBalance,
                OpenedAt = openingAt
            };
            balance.Entries.Add(new SupplierBalanceEntry
            {
                SupplierBalanceId = 0,
                SupplierBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                Amount = item.OpeningBalance,
                BalanceAfter = item.OpeningBalance,
                OccurredAt = openingAt,
                Notes = "Development seed opening balance"
            });
            db.SupplierBalances.Add(balance);
        }

        foreach (var item in data.Customers)
        {
            if (await db.Customers.AnyAsync(existing => existing.Name == item.Name, cancellationToken))
            {
                continue;
            }

            var customer = new Customer
            {
                Name = item.Name,
                NormalizedName = NameNormalization.Normalize(item.Name)
            };
            var balance = new CustomerBalance
            {
                CustomerId = 0,
                Customer = customer,
                OpeningBalance = item.OpeningBalance,
                CurrentBalance = item.OpeningBalance,
                OpenedAt = openingAt
            };
            balance.Entries.Add(new CustomerBalanceEntry
            {
                CustomerBalanceId = 0,
                CustomerBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                Amount = item.OpeningBalance,
                BalanceAfter = item.OpeningBalance,
                OccurredAt = openingAt,
                Notes = "Development seed opening balance"
            });
            db.CustomerBalances.Add(balance);
        }

        foreach (var item in data.Products)
        {
            if (await db.Products.AnyAsync(existing => existing.Barcode == item.Barcode, cancellationToken))
            {
                continue;
            }

            var product = new Product
            {
                Supplier = suppliers[item.Supplier],
                Barcode = item.Barcode,
                Name = item.Name,
                NormalizedName = NameNormalization.Normalize(item.Name),
                Unit = item.Unit,
                AllowFractionUnit = item.AllowFractionUnit,
                Category = categories[item.Category],
                CostPrice = item.CostPrice,
                SellingPrice = item.SellingPrice
            };
            db.Products.Add(product);

            foreach (var opening in item.Stock)
            {
                var location = locations[opening.Location];
                db.InventoryBalances.Add(new InventoryBalance
                {
                    ProductId = 0,
                    Product = product,
                    StockLocationId = 0,
                    StockLocation = location,
                    QuantityOnHand = opening.Quantity
                });
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = 0,
                    Product = product,
                    StockLocationId = 0,
                    StockLocation = location,
                    Type = InventoryMovementType.Opening,
                    Quantity = opening.Quantity,
                    UnitCost = item.CostPrice,
                    OccurredAt = openingAt,
                    Notes = "Development seed opening stock"
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RepairOpeningDatesAsync(
        AppDbContext db, SeedData data, DateTimeOffset openingAt, CancellationToken ct)
    {
        // Old seeds used the machine clock even when the working day was months
        // earlier. Correct only fixture-owned opening records; never reset stock,
        // balances, user adjustments, transaction dates or audit creation times.
        var barcodes = data.Products.Select(product => product.Barcode).ToArray();
        var movements = await db.InventoryMovements
            .Where(move => move.Type == InventoryMovementType.Opening
                && move.Notes == "Development seed opening stock"
                && barcodes.Contains(move.Product!.Barcode!))
            .ToArrayAsync(ct);

        foreach (var movement in movements)
        {
            movement.OccurredAt = openingAt;
        }

        var suppliers = data.Suppliers.Select(supplier => supplier.Name).ToArray();
        var supplierEntries = await db.SupplierBalanceEntries
            .Include(entry => entry.SupplierBalance)
            .Where(entry => entry.Type == BalanceEntryType.OpeningBalance
                && entry.Notes == "Development seed opening balance"
                && suppliers.Contains(entry.SupplierBalance!.Supplier!.Name))
            .ToArrayAsync(ct);

        foreach (var entry in supplierEntries)
        {
            if (entry.SupplierBalance!.OpenedAt == entry.OccurredAt)
            {
                entry.SupplierBalance.OpenedAt = openingAt;
            }

            entry.OccurredAt = openingAt;
        }

        var customers = data.Customers.Select(customer => customer.Name).ToArray();
        var customerEntries = await db.CustomerBalanceEntries
            .Include(entry => entry.CustomerBalance)
            .Where(entry => entry.Type == BalanceEntryType.OpeningBalance
                && entry.Notes == "Development seed opening balance"
                && customers.Contains(entry.CustomerBalance!.Customer!.Name))
            .ToArrayAsync(ct);

        foreach (var entry in customerEntries)
        {
            if (entry.CustomerBalance!.OpenedAt == entry.OccurredAt)
            {
                entry.CustomerBalance.OpenedAt = openingAt;
            }

            entry.OccurredAt = openingAt;
        }
    }

    private static void Validate(SeedData data)
    {
        if (data.OpeningDate == default)
        {
            throw new InvalidDataException("Set openingDate in the development seed file to the date its opening stock and balances became available.");
        }

        Unique(data.Categories.Select(item => item.Name), "category name");
        Unique(data.Locations.Select(item => item.Code), "location code");
        Unique(data.Suppliers.Select(item => item.Name), "supplier name");
        Unique(data.Customers.Select(item => item.Name), "customer name");
        Unique(data.Products.Select(item => item.Barcode), "product barcode");

        var categoryNames = data.Categories.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var locationCodes = data.Locations.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var item in data.Products)
        {
            if (!data.Suppliers.Any(supplier => supplier.Name == item.Supplier) || !categoryNames.Contains(item.Category) ||
                string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.Unit) ||
                !ValidMoney(item.CostPrice) ||
                !ValidMoney(item.SellingPrice))
            {
                throw new InvalidDataException($"Invalid development product: {item.Barcode}.");
            }

            Unique(item.Stock.Select(stock => stock.Location), $"stock location for {item.Barcode}");
            foreach (var stock in item.Stock)
            {
                if (!locationCodes.Contains(stock.Location) ||
                    stock.Quantity <= 0 ||
                    (!item.AllowFractionUnit && decimal.Truncate(stock.Quantity) != stock.Quantity) ||
                    stock.Quantity > 999999999999999.999m ||
                    decimal.Round(stock.Quantity, 3) != stock.Quantity)
                {
                    throw new InvalidDataException($"Invalid opening stock for {item.Barcode}.");
                }
            }
        }

        if (data.Suppliers.Any(item => !ValidMoney(item.OpeningBalance, allowNegative: true)) ||
            data.Customers.Any(item => !ValidMoney(item.OpeningBalance, allowNegative: true)))
        {
            throw new InvalidDataException("Invalid development opening balance.");
        }
    }

    private static void Unique(IEnumerable<string> keys, string label)
    {
        var values = keys.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new InvalidDataException($"Development seed {label}s must be nonempty and unique.");
        }
    }

    private static bool ValidMoney(decimal value, bool allowNegative = false)
    {
        return (allowNegative || value >= 0) &&
            Math.Abs(value) <= 9999999999999999.99m &&
            decimal.Round(value, 2) == value;
    }

    private sealed class SeedData
    {
        public DateOnly OpeningDate { get; init; }
        public required List<SeedCategory> Categories { get; init; }
        public required List<SeedLocation> Locations { get; init; }
        public required List<SeedSupplier> Suppliers { get; init; }
        public required List<SeedCustomer> Customers { get; init; }
        public required List<SeedProduct> Products { get; init; }
    }

    private sealed class SeedCategory
    {
        public required string Name { get; init; }
        public string? Description { get; init; }
    }

    private sealed class SeedLocation
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
    }

    private sealed class SeedSupplier
    {
        public required string Name { get; init; }
        public string? ContactName { get; init; }
        public decimal OpeningBalance { get; init; }
    }

    private sealed class SeedCustomer
    {
        public required string Name { get; init; }
        public decimal OpeningBalance { get; init; }
    }

    private sealed class SeedProduct
    {
        public required string Supplier { get; init; }
        public required string Barcode { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; }
        public required string Unit { get; init; }
        public bool AllowFractionUnit { get; init; }
        public decimal CostPrice { get; init; }
        public decimal SellingPrice { get; init; }
        public required List<SeedStock> Stock { get; init; }
    }

    private sealed class SeedStock
    {
        public required string Location { get; init; }
        public decimal Quantity { get; init; }
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence.Entities;
using POSApp.Services;

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

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The development seed file was not found at '{path}'.");
        }

        await using var file = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<SeedData>(file, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The development seed file is empty.");

        Validate(data);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var zone = AccountingCalendar.Zone(configuration);
        var openingAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            data.OpeningDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        await RepairOpeningDatesAsync(db, data, openingAt, cancellationToken);

        // Resolve Admin / Actor User
        var adminRole = await db.Roles.SingleOrDefaultAsync(r => r.Name == AppRoles.Admin, cancellationToken);
        int? adminUserId = null;
        if (adminRole is not null)
        {
            var adminUserMembership = await db.UserRoles
                .FirstOrDefaultAsync(ur => ur.RoleId == adminRole.Id, cancellationToken);
            if (adminUserMembership is not null)
            {
                adminUserId = adminUserMembership.UserId;
            }
        }
        adminUserId ??= await db.Users.Select(u => (int?)u.Id).FirstOrDefaultAsync(cancellationToken);

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
            else if (!string.IsNullOrEmpty(item.Description) && string.IsNullOrEmpty(category.Description))
            {
                category.Description = item.Description;
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
                    NormalizedName = NameNormalization.Normalize(item.Name),
                    CanSell = item.CanSell,
                    Address = item.Address,
                    Phone = item.Phone
                };
                db.StockLocations.Add(location);
            }
            else
            {
                location.CanSell = item.CanSell;
                if (!string.IsNullOrEmpty(item.Address) && string.IsNullOrEmpty(location.Address))
                {
                    location.Address = item.Address;
                }
                if (!string.IsNullOrEmpty(item.Phone) && string.IsNullOrEmpty(location.Phone))
                {
                    location.Phone = item.Phone;
                }
            }

            locations.Add(item.Code, location);
        }

        var suppliers = new Dictionary<string, Supplier>(StringComparer.Ordinal);
        foreach (var item in data.Suppliers)
        {
            var existingSupplier = await db.Suppliers.SingleOrDefaultAsync(existing => existing.Name == item.Name, cancellationToken);
            if (existingSupplier is not null)
            {
                if (!string.IsNullOrEmpty(item.ContactName) && string.IsNullOrEmpty(existingSupplier.ContactName))
                {
                    existingSupplier.ContactName = item.ContactName;
                }
                if (!string.IsNullOrEmpty(item.Email) && string.IsNullOrEmpty(existingSupplier.Email))
                {
                    existingSupplier.Email = item.Email;
                }
                if (!string.IsNullOrEmpty(item.Phone) && string.IsNullOrEmpty(existingSupplier.Phone))
                {
                    existingSupplier.Phone = item.Phone;
                }
                if (!string.IsNullOrEmpty(item.Address) && string.IsNullOrEmpty(existingSupplier.Address))
                {
                    existingSupplier.Address = item.Address;
                }
                suppliers.Add(item.Name, existingSupplier);
                continue;
            }

            var supplier = new Supplier
            {
                Name = item.Name,
                NormalizedName = NameNormalization.Normalize(item.Name),
                ContactName = item.ContactName,
                Email = item.Email,
                Phone = item.Phone,
                Address = item.Address
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
            var existingCustomer = await db.Customers.SingleOrDefaultAsync(existing => existing.Name == item.Name, cancellationToken);
            if (existingCustomer is not null)
            {
                if (!string.IsNullOrEmpty(item.Email) && string.IsNullOrEmpty(existingCustomer.Email))
                {
                    existingCustomer.Email = item.Email;
                }
                if (!string.IsNullOrEmpty(item.Phone) && string.IsNullOrEmpty(existingCustomer.Phone))
                {
                    existingCustomer.Phone = item.Phone;
                }
                if (!string.IsNullOrEmpty(item.Address) && string.IsNullOrEmpty(existingCustomer.Address))
                {
                    existingCustomer.Address = item.Address;
                }
                continue;
            }

            var customer = new Customer
            {
                Name = item.Name,
                NormalizedName = NameNormalization.Normalize(item.Name),
                Email = item.Email,
                Phone = item.Phone,
                Address = item.Address
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
            var existingProduct = await db.Products.SingleOrDefaultAsync(existing => existing.Barcode == item.Barcode, cancellationToken);
            if (existingProduct is not null)
            {
                if (!string.IsNullOrEmpty(item.Description) && string.IsNullOrEmpty(existingProduct.Description))
                {
                    existingProduct.Description = item.Description;
                }

                if (!existingProduct.IsNonStocked && existingProduct.CostPrice > 0
                    && !await db.ProductCostHistories.AnyAsync(h => h.ProductId == existingProduct.Id, cancellationToken))
                {
                    db.ProductCostHistories.Add(new ProductCostHistory
                    {
                        ProductId = existingProduct.Id,
                        OldCostPrice = 0,
                        NewCostPrice = existingProduct.CostPrice,
                        Reason = "Development seed initial cost",
                        ReferenceType = "Seed",
                        CreatedAt = openingAt,
                        CreatedByUserId = adminUserId
                    });
                }
                continue;
            }

            var product = new Product
            {
                Supplier = suppliers[item.Supplier],
                Barcode = item.Barcode,
                Name = item.Name,
                NormalizedName = NameNormalization.Normalize(item.Name),
                Description = item.Description,
                Unit = item.Unit,
                AllowFractionUnit = item.AllowFractionUnit,
                IsNonStocked = item.IsNonStocked,
                Category = categories[item.Category],
                CostPrice = item.IsNonStocked ? 0m : item.CostPrice,
                SellingPrice = item.SellingPrice
            };
            db.Products.Add(product);

            if (!item.IsNonStocked && product.CostPrice > 0)
            {
                db.ProductCostHistories.Add(new ProductCostHistory
                {
                    Product = product,
                    OldCostPrice = 0,
                    NewCostPrice = product.CostPrice,
                    Reason = "Development seed initial cost",
                    ReferenceType = "Seed",
                    CreatedAt = openingAt,
                    CreatedByUserId = adminUserId
                });
            }

            if (!item.IsNonStocked && item.Stock is { Count: > 0 })
            {
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
        }

        await db.SaveChangesAsync(cancellationToken);

        // Ensure active working day
        var activeDate = await AccountingCalendar.SelectedAsync(db, adminUserId ?? 1, configuration, cancellationToken);
        var workingDay = await db.WorkingDays.SingleOrDefaultAsync(d => d.Id == 1, cancellationToken);
        if (workingDay is null)
        {
            workingDay = new WorkingDay { Id = 1, Date = activeDate };
            db.WorkingDays.Add(workingDay);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Primary Selling Location
        var sellingLocationCode = data.CashDrawer?.Location
            ?? data.Locations.FirstOrDefault(l => l.CanSell)?.Code
            ?? "DEMO-MAIN";

        if (locations.TryGetValue(sellingLocationCode, out var mainSellingLocation) && mainSellingLocation.CanSell)
        {
            // Assign unassigned users to primary selling location
            var unassignedUsers = await db.Users.Where(u => u.CashLocationId == null).ToListAsync(cancellationToken);
            foreach (var user in unassignedUsers)
            {
                user.CashLocationId = mainSellingLocation.Id;
            }

            // Ensure CashDrawer exists
            var drawer = await db.CashDrawers
                .Include(d => d.CurrentSession)
                .SingleOrDefaultAsync(d => d.StockLocationId == mainSellingLocation.Id, cancellationToken);

            var openingCash = data.CashDrawer?.OpeningBalance ?? 10000m;
            if (drawer is null)
            {
                drawer = new CashDrawer
                {
                    StockLocationId = mainSellingLocation.Id,
                    OpeningBalance = openingCash,
                    OpeningDate = activeDate,
                    CurrentCash = openingCash,
                    CreatedByUserId = adminUserId
                };
                db.CashDrawers.Add(drawer);
                await db.SaveChangesAsync(cancellationToken);
            }

            // Ensure DayFinance exists for active working day
            var dayFinance = await db.DayFinances.SingleOrDefaultAsync(df => df.Date == activeDate, cancellationToken);
            if (dayFinance is null)
            {
                dayFinance = new DayFinance
                {
                    Date = activeDate,
                    OpeningCash = drawer.CurrentCash,
                    ExpectedCash = drawer.CurrentCash,
                    CreatedByUserId = adminUserId
                };
                db.DayFinances.Add(dayFinance);
                await db.SaveChangesAsync(cancellationToken);
            }

            // Ensure CashSession is open for active working day
            if (drawer.CurrentSession is null)
            {
                var openSession = await db.CashSessions
                    .SingleOrDefaultAsync(s => s.CashDrawerId == drawer.Id && s.ClosedAt == null, cancellationToken);

                if (openSession is null && !await db.CashSessions.AnyAsync(s => s.CashDrawerId == drawer.Id && s.BusinessDate == activeDate, cancellationToken))
                {
                    openSession = new CashSession
                    {
                        RequestId = Guid.NewGuid(),
                        CashDrawerId = drawer.Id,
                        StockLocationId = mainSellingLocation.Id,
                        BusinessDate = activeDate,
                        OpenedAt = DateTimeOffset.UtcNow,
                        OpeningCash = drawer.CurrentCash,
                        ExpectedCash = drawer.CurrentCash,
                        CreatedByUserId = adminUserId
                    };
                    db.CashSessions.Add(openSession);
                    await db.SaveChangesAsync(cancellationToken);

                    drawer.CurrentSessionId = openSession.Id;
                    drawer.CurrentSession = openSession;
                    await db.SaveChangesAsync(cancellationToken);
                }
                else if (openSession is not null)
                {
                    drawer.CurrentSessionId = openSession.Id;
                    drawer.CurrentSession = openSession;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // Bank Account
        var bankCode = data.BankAccount?.Code ?? "MAIN";
        var bankAccount = await db.BankAccounts.SingleOrDefaultAsync(b => b.Code == bankCode, cancellationToken);
        var bankOpeningBalance = data.BankAccount?.OpeningBalance ?? 50000m;
        if (bankAccount is null)
        {
            bankAccount = new BankAccount
            {
                Code = bankCode,
                OpeningBalance = bankOpeningBalance,
                OpeningDate = data.OpeningDate,
                CurrentBalance = bankOpeningBalance,
                CreatedByUserId = adminUserId
            };
            db.BankAccounts.Add(bankAccount);
            await db.SaveChangesAsync(cancellationToken);
        }

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

    internal static void Validate(SeedData data)
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

        if (!data.Locations.Any(l => l.CanSell))
        {
            throw new InvalidDataException("At least one location must have canSell set to true.");
        }

        if (data.CashDrawer is not null)
        {
            var drawerLocation = data.Locations.FirstOrDefault(l => l.Code == data.CashDrawer.Location);
            if (drawerLocation is null || !drawerLocation.CanSell)
            {
                throw new InvalidDataException($"Cash drawer location {data.CashDrawer.Location} must be a valid selling location.");
            }
            if (!ValidMoney(data.CashDrawer.OpeningBalance))
            {
                throw new InvalidDataException("Invalid cash drawer opening balance.");
            }
        }

        if (data.BankAccount is not null)
        {
            if (string.IsNullOrWhiteSpace(data.BankAccount.Code) || !ValidMoney(data.BankAccount.OpeningBalance))
            {
                throw new InvalidDataException("Invalid bank account opening balance.");
            }
        }

        foreach (var item in data.Products)
        {
            if (!data.Suppliers.Any(supplier => supplier.Name == item.Supplier) ||
                !categoryNames.Contains(item.Category) ||
                string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.Unit) ||
                !ValidMoney(item.CostPrice) ||
                !ValidMoney(item.SellingPrice))
            {
                throw new InvalidDataException($"Invalid development product: {item.Barcode}.");
            }

            if (item.IsNonStocked)
            {
                if (item.Stock.Count > 0)
                {
                    throw new InvalidDataException($"Non-stocked product {item.Barcode} cannot have opening stock.");
                }
                if (item.CostPrice != 0)
                {
                    throw new InvalidDataException($"Non-stocked product {item.Barcode} must have zero cost price.");
                }
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

    internal sealed class SeedData
    {
        public DateOnly OpeningDate { get; init; }
        public SeedCashDrawer? CashDrawer { get; init; }
        public SeedBankAccount? BankAccount { get; init; }
        public required List<SeedCategory> Categories { get; init; }
        public required List<SeedLocation> Locations { get; init; }
        public required List<SeedSupplier> Suppliers { get; init; }
        public required List<SeedCustomer> Customers { get; init; }
        public required List<SeedProduct> Products { get; init; }
    }

    internal sealed class SeedCashDrawer
    {
        public required string Location { get; init; }
        public decimal OpeningBalance { get; init; }
    }

    internal sealed class SeedBankAccount
    {
        public string Code { get; init; } = "MAIN";
        public decimal OpeningBalance { get; init; }
    }

    internal sealed class SeedCategory
    {
        public required string Name { get; init; }
        public string? Description { get; init; }
    }

    internal sealed class SeedLocation
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public bool CanSell { get; init; } = true;
        public string? Address { get; init; }
        public string? Phone { get; init; }
    }

    internal sealed class SeedSupplier
    {
        public required string Name { get; init; }
        public string? ContactName { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public string? Address { get; init; }
        public decimal OpeningBalance { get; init; }
    }

    internal sealed class SeedCustomer
    {
        public required string Name { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public string? Address { get; init; }
        public decimal OpeningBalance { get; init; }
    }

    internal sealed class SeedProduct
    {
        public required string Supplier { get; init; }
        public required string Barcode { get; init; }
        public required string Name { get; init; }
        public string? Description { get; init; }
        public required string Category { get; init; }
        public required string Unit { get; init; }
        public bool AllowFractionUnit { get; init; }
        public bool IsNonStocked { get; init; }
        public decimal CostPrice { get; init; }
        public decimal SellingPrice { get; init; }
        public List<SeedStock> Stock { get; init; } = [];
    }

    internal sealed class SeedStock
    {
        public required string Location { get; init; }
        public decimal Quantity { get; init; }
    }
}

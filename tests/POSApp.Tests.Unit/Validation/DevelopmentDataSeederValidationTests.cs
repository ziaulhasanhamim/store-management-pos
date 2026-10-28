using POSApp.Persistence;
using static POSApp.Persistence.DevelopmentDataSeeder;

namespace POSApp.Tests.Unit.Validation;

public class DevelopmentDataSeederValidationTests
{
    private static SeedData CreateValidData() => new()
    {
        OpeningDate = new DateOnly(2026, 1, 1),
        CashDrawer = new SeedCashDrawer { Location = "MAIN", OpeningBalance = 1000m },
        BankAccount = new SeedBankAccount { Code = "MAIN", OpeningBalance = 5000m },
        Categories = [new SeedCategory { Name = "General", Description = "General items" }],
        Locations = [
            new SeedLocation { Code = "MAIN", Name = "Main Store", CanSell = true },
            new SeedLocation { Code = "BACK", Name = "Back Room", CanSell = false }
        ],
        Suppliers = [new SeedSupplier { Name = "Default Supplier", OpeningBalance = 0m }],
        Customers = [new SeedCustomer { Name = "Walk-in Customer", OpeningBalance = 0m }],
        Products = [
            new SeedProduct
            {
                Barcode = "PROD-1",
                Name = "Product 1",
                Category = "General",
                Supplier = "Default Supplier",
                Unit = "piece",
                AllowFractionUnit = false,
                CostPrice = 10m,
                SellingPrice = 20m,
                Stock = [new SeedStock { Location = "MAIN", Quantity = 5m }]
            }
        ]
    };

    [Test]
    public void Validate_ValidSeedData_Passes()
    {
        var data = CreateValidData();
        DevelopmentDataSeeder.Validate(data);
    }

    [Test]
    public void Validate_DefaultOpeningDate_Throws()
    {
        var data = new SeedData
        {
            OpeningDate = default,
            Categories = [new SeedCategory { Name = "General" }],
            Locations = [new SeedLocation { Code = "MAIN", Name = "Main Store", CanSell = true }],
            Suppliers = [],
            Customers = [],
            Products = []
        };

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(data));
    }

    [Test]
    public void Validate_NoSellingLocations_Throws()
    {
        var data = CreateValidData();
        data.Locations[0] = new SeedLocation { Code = "MAIN", Name = "Main Store", CanSell = false };

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(data));
    }

    [Test]
    public void Validate_CashDrawerLocationNotSelling_Throws()
    {
        var data = CreateValidData();
        data.Locations[0] = new SeedLocation { Code = "MAIN", Name = "Main Store", CanSell = true };
        data.Locations[1] = new SeedLocation { Code = "BACK", Name = "Back Room", CanSell = false };
        // CashDrawer pointing to BACK which has CanSell = false
        var modifiedData = new SeedData
        {
            OpeningDate = data.OpeningDate,
            CashDrawer = new SeedCashDrawer { Location = "BACK", OpeningBalance = 1000m },
            BankAccount = data.BankAccount,
            Categories = data.Categories,
            Locations = data.Locations,
            Suppliers = data.Suppliers,
            Customers = data.Customers,
            Products = data.Products
        };

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(modifiedData));
    }

    [Test]
    public void Validate_NonStockedProductWithStock_Throws()
    {
        var data = CreateValidData();
        data.Products.Add(new SeedProduct
        {
            Barcode = "SERV-1",
            Name = "Service",
            Category = "General",
            Supplier = "Default Supplier",
            Unit = "hour",
            IsNonStocked = true,
            CostPrice = 0m,
            SellingPrice = 50m,
            Stock = [new SeedStock { Location = "MAIN", Quantity = 2m }]
        });

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(data));
    }

    [Test]
    public void Validate_NonStockedProductWithNonZeroCost_Throws()
    {
        var data = CreateValidData();
        data.Products.Add(new SeedProduct
        {
            Barcode = "SERV-1",
            Name = "Service",
            Category = "General",
            Supplier = "Default Supplier",
            Unit = "hour",
            IsNonStocked = true,
            CostPrice = 25m,
            SellingPrice = 50m,
            Stock = []
        });

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(data));
    }

    [Test]
    public void Validate_FractionalStockForWholeUnitProduct_Throws()
    {
        var data = CreateValidData();
        data.Products[0] = new SeedProduct
        {
            Barcode = "PROD-1",
            Name = "Product 1",
            Category = "General",
            Supplier = "Default Supplier",
            Unit = "piece",
            AllowFractionUnit = false,
            CostPrice = 10m,
            SellingPrice = 20m,
            Stock = [new SeedStock { Location = "MAIN", Quantity = 2.5m }]
        };

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(data));
    }

    [Test]
    public void Validate_DuplicateBarcodes_Throws()
    {
        var data = CreateValidData();
        data.Products.Add(new SeedProduct
        {
            Barcode = "PROD-1", // duplicate
            Name = "Product 2",
            Category = "General",
            Supplier = "Default Supplier",
            Unit = "piece",
            AllowFractionUnit = false,
            CostPrice = 10m,
            SellingPrice = 20m,
            Stock = []
        });

        Assert.Throws<InvalidDataException>(() => DevelopmentDataSeeder.Validate(data));
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace POSApp.Tests.Integration.Infrastructure;

public static class PostgreSqlTestDatabase
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static Respawner? _respawner;
    private static bool _initialized;

    public static string ConnectionString { get; private set; } = string.Empty;

    public static async Task InitializeAsync()
    {
        if (_initialized) return;

        await Lock.WaitAsync();
        try
        {
            if (_initialized) return;

            var testDbUrl = Environment.GetEnvironmentVariable("TEST_DB_URL");
            if (!string.IsNullOrWhiteSpace(testDbUrl))
            {
                ConnectionString = testDbUrl;
            }
            else
            {
                _container = new PostgreSqlBuilder("postgres:16-alpine")
                    .WithDatabase("posapp_test")
                    .WithUsername("postgres")
                    .WithPassword("postgres")
                    .Build();

                await _container.StartAsync();
                ConnectionString = _container.GetConnectionString();
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
            }

            await using (var connection = new NpgsqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
                {
                    DbAdapter = DbAdapter.Postgres,
                    SchemasToInclude = ["public"],
                    TablesToIgnore = [new Table("__EFMigrationsHistory")]
                });
            }

            await ResetAsync();
            _initialized = true;
        }
        finally
        {
            Lock.Release();
        }
    }

    public static async Task ResetAsync()
    {
        if (_respawner is null)
        {
            await InitializeAsync();
            return;
        }

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await _respawner.ResetAsync(connection);
        }

        await SeedBaseDataAsync();
    }

    private static async Task SeedBaseDataAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var db = new AppDbContext(options);

        // Seed Roles
        var adminRole = new IdentityRole<int>(AppRoles.Admin) { Id = 1, NormalizedName = AppRoles.Admin.ToUpperInvariant() };
        var inventoryRole = new IdentityRole<int>(AppRoles.InventoryManager) { Id = 2, NormalizedName = AppRoles.InventoryManager.ToUpperInvariant() };
        var salesRole = new IdentityRole<int>(AppRoles.SalesAssociate) { Id = 3, NormalizedName = AppRoles.SalesAssociate.ToUpperInvariant() };
        db.Roles.AddRange(adminRole, inventoryRole, salesRole);

        // Seed Users
        var hasher = new PasswordHasher<User>();

        var adminUser = new User
        {
            Id = 1,
            UserName = "admin",
            NormalizedUserName = "ADMIN",
            CashLocationId = 1,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D")
        };
        adminUser.PasswordHash = hasher.HashPassword(adminUser, "Admin@12345!");

        var inventoryUser = new User
        {
            Id = 2,
            UserName = "inventory",
            NormalizedUserName = "INVENTORY",
            CashLocationId = 1,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D")
        };
        inventoryUser.PasswordHash = hasher.HashPassword(inventoryUser, "Inventory@12345!");

        var salesUser = new User
        {
            Id = 3,
            UserName = "sales",
            NormalizedUserName = "SALES",
            CashLocationId = 1,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D")
        };
        salesUser.PasswordHash = hasher.HashPassword(salesUser, "Sales@12345!");

        db.Users.AddRange(adminUser, inventoryUser, salesUser);

        // Seed UserRoles
        db.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = 1, RoleId = 1 },
            new IdentityUserRole<int> { UserId = 2, RoleId = 2 },
            new IdentityUserRole<int> { UserId = 3, RoleId = 3 }
        );

        // Seed Default Stock Location
        var location = new StockLocation
        {
            Id = 1,
            Code = "MAIN",
            Name = "Main Store",
            NormalizedName = "MAIN STORE",
            CanSell = true
        };
        db.StockLocations.Add(location);

        // Seed Default Category
        var category = new Category
        {
            Id = 1,
            Name = "General",
            NormalizedName = "GENERAL"
        };
        db.Categories.Add(category);

        // Seed Default Supplier
        var supplier = new Supplier
        {
            Id = 1,
            Name = "Default Supplier",
            NormalizedName = "DEFAULT SUPPLIER"
        };
        db.Suppliers.Add(supplier);

        // Seed Default Customer
        var customer = new Customer
        {
            Id = 1,
            Name = "Walk-in Customer",
            NormalizedName = "WALK-IN CUSTOMER"
        };
        db.Customers.Add(customer);

        // Seed Working Day
        var workingDay = new WorkingDay
        {
            Id = 1,
            Date = AccountingCalendar.Today(null)
        };
        db.WorkingDays.Add(workingDay);

        // Seed Default Product
        var product = new Product
        {
            Id = 1,
            Name = "Default Product",
            NormalizedName = "DEFAULT PRODUCT",
            Unit = "piece",
            CostPrice = 100m,
            SellingPrice = 150m,
            SupplierId = 1,
            CategoryId = 1,
            Barcode = "00000017"
        };
        db.Products.Add(product);

        await db.SaveChangesAsync();

        // Seed Cash Drawer and open Session for Main Store
        var drawer = new CashDrawer
        {
            Id = 1,
            StockLocationId = 1,
            OpeningBalance = 100000m,
            CurrentCash = 100000m,
            OpeningDate = AccountingCalendar.Today(null),
            CreatedByUserId = 1
        };
        db.CashDrawers.Add(drawer);
        await db.SaveChangesAsync();

        var session = new CashSession
        {
            Id = 1,
            CashDrawerId = 1,
            StockLocationId = 1,
            BusinessDate = AccountingCalendar.Today(null),
            OpenedAt = DateTimeOffset.UtcNow,
            OpeningCash = 100000m,
            ExpectedCash = 100000m,
            ExpectedClosingCash = 100000m,
            RequestId = Guid.NewGuid(),
            CreatedByUserId = 1
        };
        db.CashSessions.Add(session);
        await db.SaveChangesAsync();

        drawer.CurrentSessionId = 1;
        await db.SaveChangesAsync();

        // Advance PostgreSQL identity sequences so subsequent inserts do not collide with seed IDs
        await db.Database.ExecuteSqlRawAsync(@"
            SELECT setval(pg_get_serial_sequence('""AspNetRoles""', 'Id'), coalesce(max(""Id""), 1)) FROM ""AspNetRoles"";
            SELECT setval(pg_get_serial_sequence('""AspNetUsers""', 'Id'), coalesce(max(""Id""), 1)) FROM ""AspNetUsers"";
            SELECT setval(pg_get_serial_sequence('""StockLocations""', 'Id'), coalesce(max(""Id""), 1)) FROM ""StockLocations"";
            SELECT setval(pg_get_serial_sequence('""Categories""', 'Id'), coalesce(max(""Id""), 1)) FROM ""Categories"";
            SELECT setval(pg_get_serial_sequence('""Suppliers""', 'Id'), coalesce(max(""Id""), 1)) FROM ""Suppliers"";
            SELECT setval(pg_get_serial_sequence('""Customers""', 'Id'), coalesce(max(""Id""), 1)) FROM ""Customers"";
            SELECT setval(pg_get_serial_sequence('""WorkingDays""', 'Id'), coalesce(max(""Id""), 1)) FROM ""WorkingDays"";
            SELECT setval(pg_get_serial_sequence('""Products""', 'Id'), coalesce(max(""Id""), 1)) FROM ""Products"";
            SELECT setval(pg_get_serial_sequence('""CashDrawers""', 'Id'), coalesce(max(""Id""), 1)) FROM ""CashDrawers"";
            SELECT setval(pg_get_serial_sequence('""CashSessions""', 'Id'), coalesce(max(""Id""), 1)) FROM ""CashSessions"";
        ");
    }
}

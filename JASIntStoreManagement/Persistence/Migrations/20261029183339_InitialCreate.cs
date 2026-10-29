using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace JASIntStoreManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BuiltInCategory = table.Column<int>(type: "integer", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceClosings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerContributions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OwnerWithdrawals = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StockVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtBorrowed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtRepaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OutstandingDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MonthlyDetailsJson = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningBank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningStock = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningSupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningCustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningNetAssets = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningAssetsCustomized = table.Column<bool>(type: "boolean", nullable: false),
                    CashIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingBank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingStock = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingSupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingNetAssets = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AssetBuilt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalaryExpense = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayrollPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StockValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalesReceipts = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerCollections = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdvancesPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceClosings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialEntryBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialEntryBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lenders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AllowFraction = table.Column<bool>(type: "boolean", nullable: false),
                    NonNegative = table.Column<bool>(type: "boolean", nullable: false),
                    ShowInItems = table.Column<bool>(type: "boolean", nullable: false),
                    ShowInSales = table.Column<bool>(type: "boolean", nullable: false),
                    ShowInReceiving = table.Column<bool>(type: "boolean", nullable: false),
                    ShowInStockAdjustment = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Before = table.Column<string>(type: "text", nullable: false),
                    After = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CanSell = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegacyId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContactName = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkingDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBalances_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonthlySalaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BroughtForward = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CarriedForward = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CarriedToMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlySalaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlySalaries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    OutstandingAdvance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffAccounts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    CategoryDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceiptReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_CategoryDefinitionId",
                        column: x => x.CategoryDefinitionId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DayFinances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CountedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinanceClosingId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayFinances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DayFinances_FinanceClosings_FinanceClosingId",
                        column: x => x.FinanceClosingId,
                        principalTable: "FinanceClosings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LenderBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LenderId = table.Column<int>(type: "integer", nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LenderBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LenderBalances_Lenders_LenderId",
                        column: x => x.LenderId,
                        principalTable: "Lenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductAttributeDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductAttributeOptions_ProductAttributeDefinitions_Product~",
                        column: x => x.ProductAttributeDefinitionId,
                        principalTable: "ProductAttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashLocationId = table.Column<int>(type: "integer", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_StockLocations_CashLocationId",
                        column: x => x.CashLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTakes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CompletionRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTakes_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfers_StockLocations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_StockLocations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DefaultCommissionRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Technicians_StockLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowFractionUnit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsNonStocked = table.Column<bool>(type: "boolean", nullable: false),
                    CostPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Purchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PurchaseNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OrderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Purchases_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Purchases_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBalances_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffAccountId = table.Column<int>(type: "integer", nullable: false),
                    MonthlySalaryId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PayrollMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdvanceApplied = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdvanceCreated = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdvanceBalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollEntries_MonthlySalaries_MonthlySalaryId",
                        column: x => x.MonthlySalaryId,
                        principalTable: "MonthlySalaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEntries_StaffAccounts_StaffAccountId",
                        column: x => x.StaffAccountId,
                        principalTable: "StaffAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DebtEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    LenderBalanceId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: true),
                    CashLocationId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtEntries_LenderBalances_LenderBalanceId",
                        column: x => x.LenderBalanceId,
                        principalTable: "LenderBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DebtEntries_StockLocations_CashLocationId",
                        column: x => x.CashLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditorDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SaveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActiveKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Result = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportJobs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    CashierId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SoldAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenderedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DraftPaymentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DraftPaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_AspNetUsers_CashierId",
                        column: x => x.CashierId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductAttributeDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DisplayValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_ProductAttributeDefinitions_ProductA~",
                        column: x => x.ProductAttributeDefinitionId,
                        principalTable: "ProductAttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductCostHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    OldCostPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NewCostPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReferenceId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCostHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductCostHistories_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCostHistories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustmentItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockAdjustmentId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustmentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentItems_StockAdjustments_StockAdjustmentId",
                        column: x => x.StockAdjustmentId,
                        principalTable: "StockAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTakeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockTakeId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowFractionUnit = table.Column<bool>(type: "boolean", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    IsIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LastMovementId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTakeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTakeItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTakeItems_StockTakes_StockTakeId",
                        column: x => x.StockTakeId,
                        principalTable: "StockTakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockTransferId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurchaseId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    ProductSku = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseItems_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupplierBalanceId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseId = table.Column<int>(type: "integer", nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_SupplierBalances_SupplierBalanceId",
                        column: x => x.SupplierBalanceId,
                        principalTable: "SupplierBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SaleId = table.Column<int>(type: "integer", nullable: true),
                    CustomerBalanceId = table.Column<int>(type: "integer", nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenderedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_CustomerBalances_CustomerBalanceId",
                        column: x => x.CustomerBalanceId,
                        principalTable: "CustomerBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleDraftPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SaleId = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleDraftPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleDraftPayments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SaleId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    ProductSku = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitDiscount = table.Column<decimal>(type: "numeric(38,20)", precision: 38, scale: 20, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "(\"UnitPrice\" - \"UnitDiscount\") * \"Quantity\"", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleItems_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierBalanceEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupplierBalanceId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PurchaseId = table.Column<int>(type: "integer", nullable: true),
                    SupplierPaymentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBalanceEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBalanceEntries_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBalanceEntries_SupplierBalances_SupplierBalanceId",
                        column: x => x.SupplierBalanceId,
                        principalTable: "SupplierBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBalanceEntries_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    SupplierPaymentId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseId = table.Column<int>(type: "integer", nullable: true),
                    DebtEntryId = table.Column<int>(type: "integer", nullable: true),
                    PayrollEntryId = table.Column<int>(type: "integer", nullable: true),
                    ReversesMovementId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankMovements_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_BankMovements_ReversesMovementId",
                        column: x => x.ReversesMovementId,
                        principalTable: "BankMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_DebtEntries_DebtEntryId",
                        column: x => x.DebtEntryId,
                        principalTable: "DebtEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_PayrollEntries_PayrollEntryId",
                        column: x => x.PayrollEntryId,
                        principalTable: "PayrollEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBalanceEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CustomerBalanceId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SaleId = table.Column<int>(type: "integer", nullable: true),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBalanceEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBalanceEntries_CustomerBalances_CustomerBalanceId",
                        column: x => x.CustomerBalanceId,
                        principalTable: "CustomerBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerBalanceEntries_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerBalanceEntries_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    StockAdjustmentItemId = table.Column<int>(type: "integer", nullable: true),
                    StockTakeItemId = table.Column<int>(type: "integer", nullable: true),
                    StockTransferId = table.Column<int>(type: "integer", nullable: true),
                    SaleItemId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseItemId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_PurchaseItems_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "PurchaseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_SaleItems_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StockAdjustmentItems_StockAdjustmentItem~",
                        column: x => x.StockAdjustmentItemId,
                        principalTable: "StockAdjustmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StockTakeItems_StockTakeItemId",
                        column: x => x.StockTakeItemId,
                        principalTable: "StockTakeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashDrawers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockLocationId = table.Column<int>(type: "integer", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentSessionId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashDrawers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashDrawers_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CashDrawerId = table.Column<int>(type: "integer", nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedClosingCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ActualClosingCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosingNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashSessions_CashDrawers_CashDrawerId",
                        column: x => x.CashDrawerId,
                        principalTable: "CashDrawers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSessions_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDrawerId = table.Column<int>(type: "integer", nullable: false),
                    DestinationDrawerId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashTransfers_CashDrawers_DestinationDrawerId",
                        column: x => x.DestinationDrawerId,
                        principalTable: "CashDrawers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransfers_CashDrawers_SourceDrawerId",
                        column: x => x.SourceDrawerId,
                        principalTable: "CashDrawers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashTransferId = table.Column<int>(type: "integer", nullable: true),
                    CashSessionId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    SupplierPaymentId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseId = table.Column<int>(type: "integer", nullable: true),
                    DebtEntryId = table.Column<int>(type: "integer", nullable: true),
                    PayrollEntryId = table.Column<int>(type: "integer", nullable: true),
                    ReversesMovementId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashMovements_ReversesMovementId",
                        column: x => x.ReversesMovementId,
                        principalTable: "CashMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashTransfers_CashTransferId",
                        column: x => x.CashTransferId,
                        principalTable: "CashTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_DebtEntries_DebtEntryId",
                        column: x => x.DebtEntryId,
                        principalTable: "DebtEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_PayrollEntries_PayrollEntryId",
                        column: x => x.PayrollEntryId,
                        principalTable: "PayrollEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MobileServicingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    TechnicianId = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TechnicianCut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShopProfit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsDeposited = table.Column<bool>(type: "boolean", nullable: false),
                    DepositedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CashMovementId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileServicingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobileServicingEntries_CashMovements_CashMovementId",
                        column: x => x.CashMovementId,
                        principalTable: "CashMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobileServicingEntries_StockLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobileServicingEntries_Technicians_TechnicianId",
                        column: x => x.TechnicianId,
                        principalTable: "Technicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ExpenseCategories",
                columns: new[] { "Id", "BuiltInCategory", "IsArchived", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { -7, 7, false, "Other", "OTHER" },
                    { -6, 6, false, "Maintenance", "MAINTENANCE" },
                    { -5, 5, false, "Rent", "RENT" },
                    { -4, 4, false, "Packaging", "PACKAGING" },
                    { -3, 3, false, "Transport", "TRANSPORT" },
                    { -2, 2, false, "Snacks / refreshments", "SNACKS / REFRESHMENTS" },
                    { -1, 1, false, "Utilities", "UTILITIES" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CashLocationId",
                table: "AspNetUsers",
                column: "CashLocationId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_Code",
                table: "BankAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BankAccountId",
                table: "BankMovements",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BusinessDate",
                table: "BankMovements",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_DebtEntryId",
                table: "BankMovements",
                column: "DebtEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_ExpenseId",
                table: "BankMovements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_PaymentId",
                table: "BankMovements",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_PayrollEntryId",
                table: "BankMovements",
                column: "PayrollEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_RequestId",
                table: "BankMovements",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_ReversesMovementId",
                table: "BankMovements",
                column: "ReversesMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_SupplierPaymentId",
                table: "BankMovements",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawers_CurrentSessionId",
                table: "CashDrawers",
                column: "CurrentSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawers_StockLocationId",
                table: "CashDrawers",
                column: "StockLocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_BusinessDate",
                table: "CashMovements",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashSessionId_Id",
                table: "CashMovements",
                columns: new[] { "CashSessionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashTransferId",
                table: "CashMovements",
                column: "CashTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_DebtEntryId",
                table: "CashMovements",
                column: "DebtEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ExpenseId",
                table: "CashMovements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_PaymentId",
                table: "CashMovements",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_PayrollEntryId",
                table: "CashMovements",
                column: "PayrollEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_RequestId",
                table: "CashMovements",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ReversesMovementId",
                table: "CashMovements",
                column: "ReversesMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_SupplierPaymentId",
                table: "CashMovements",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_CashDrawerId_BusinessDate",
                table: "CashSessions",
                columns: new[] { "CashDrawerId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_CashDrawerId_OpenedAt",
                table: "CashSessions",
                columns: new[] { "CashDrawerId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_RequestId",
                table: "CashSessions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_StockLocationId",
                table: "CashSessions",
                column: "StockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_DestinationDrawerId",
                table: "CashTransfers",
                column: "DestinationDrawerId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_RequestId",
                table: "CashTransfers",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_SourceDrawerId",
                table: "CashTransfers",
                column: "SourceDrawerId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NormalizedName",
                table: "Categories",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_CustomerBalanceId",
                table: "CustomerBalanceEntries",
                column: "CustomerBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_PaymentId",
                table: "CustomerBalanceEntries",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_RequestId",
                table: "CustomerBalanceEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_SaleId",
                table: "CustomerBalanceEntries",
                column: "SaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalances_CustomerId",
                table: "CustomerBalances",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_DayFinances_Date",
                table: "DayFinances",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayFinances_FinanceClosingId",
                table: "DayFinances",
                column: "FinanceClosingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_BusinessDate",
                table: "DebtEntries",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_CashLocationId",
                table: "DebtEntries",
                column: "CashLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_LenderBalanceId_BusinessDate",
                table: "DebtEntries",
                columns: new[] { "LenderBalanceId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_RequestId",
                table: "DebtEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorDrafts_UserId_Kind",
                table: "EditorDrafts",
                columns: new[] { "UserId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NormalizedName",
                table: "Employees",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_NormalizedName",
                table: "ExpenseCategories",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryDefinitionId",
                table: "Expenses",
                column: "CategoryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OccurredAt",
                table: "Expenses",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_RequestId",
                table: "Expenses",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceClosings_Kind_Date",
                table: "FinanceClosings",
                columns: new[] { "Kind", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEntryBatches_RequestId",
                table: "FinancialEntryBatches",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_ActiveKey",
                table: "ImportJobs",
                column: "ActiveKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_Kind_CreatedAt",
                table: "ImportJobs",
                columns: new[] { "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_UserId",
                table: "ImportJobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_ProductId",
                table: "InventoryBalances",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_StockLocationId_ProductId",
                table: "InventoryBalances",
                columns: new[] { "StockLocationId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductId",
                table: "InventoryMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_PurchaseItemId",
                table: "InventoryMovements",
                column: "PurchaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SaleItemId",
                table: "InventoryMovements",
                column: "SaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockAdjustmentItemId",
                table: "InventoryMovements",
                column: "StockAdjustmentItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockLocationId_ProductId_OccurredAt",
                table: "InventoryMovements",
                columns: new[] { "StockLocationId", "ProductId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockTakeItemId",
                table: "InventoryMovements",
                column: "StockTakeItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockTransferId",
                table: "InventoryMovements",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_LenderBalances_LenderId",
                table: "LenderBalances",
                column: "LenderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lenders_NormalizedName",
                table: "Lenders",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Lenders_RequestId",
                table: "Lenders",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileServicingEntries_CashMovementId",
                table: "MobileServicingEntries",
                column: "CashMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileServicingEntries_Date",
                table: "MobileServicingEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_MobileServicingEntries_IsDeposited",
                table: "MobileServicingEntries",
                column: "IsDeposited");

            migrationBuilder.CreateIndex(
                name: "IX_MobileServicingEntries_LocationId_Date",
                table: "MobileServicingEntries",
                columns: new[] { "LocationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MobileServicingEntries_TechnicianId_Date",
                table: "MobileServicingEntries",
                columns: new[] { "TechnicianId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalaries_EmployeeId_Month",
                table: "MonthlySalaries",
                columns: new[] { "EmployeeId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalaries_OccurredAt",
                table: "MonthlySalaries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CustomerBalanceId_PaidAt",
                table: "Payments",
                columns: new[] { "CustomerBalanceId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Reference",
                table: "Payments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SaleId_PaidAt",
                table: "Payments",
                columns: new[] { "SaleId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_MonthlySalaryId",
                table: "PayrollEntries",
                column: "MonthlySalaryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_OccurredAt",
                table: "PayrollEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_RequestId",
                table: "PayrollEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_StaffAccountId_OccurredAt",
                table: "PayrollEntries",
                columns: new[] { "StaffAccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeDefinitions_Key",
                table: "ProductAttributeDefinitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeOptions_ProductAttributeDefinitionId_Value",
                table: "ProductAttributeOptions",
                columns: new[] { "ProductAttributeDefinitionId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_ProductAttributeDefinitionId",
                table: "ProductAttributeValues",
                column: "ProductAttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_ProductId_ProductAttributeDefinition~",
                table: "ProductAttributeValues",
                columns: new[] { "ProductId", "ProductAttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostHistories_CreatedByUserId",
                table: "ProductCostHistories",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostHistories_ProductId_CreatedAt",
                table: "ProductCostHistories",
                columns: new[] { "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Barcode",
                table: "Products",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_NormalizedName",
                table: "Products",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SupplierId",
                table: "Products",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseItems_ProductId",
                table: "PurchaseItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseItems_PurchaseId",
                table: "PurchaseItems",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PurchaseNumber",
                table: "Purchases",
                column: "PurchaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ReceivedAt",
                table: "Purchases",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_StockLocationId_OrderedAt",
                table: "Purchases",
                columns: new[] { "StockLocationId", "OrderedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_StockLocationId_ReceivedAt",
                table: "Purchases",
                columns: new[] { "StockLocationId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId_SupplierInvoiceNumber",
                table: "Purchases",
                columns: new[] { "SupplierId", "SupplierInvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordChanges_RecordType_RecordId_Id",
                table: "RecordChanges",
                columns: new[] { "RecordType", "RecordId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordChanges_RequestId",
                table: "RecordChanges",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleDraftPayments_SaleId",
                table: "SaleDraftPayments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_ProductId",
                table: "SaleItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_SaleId",
                table: "SaleItems",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CashierId",
                table: "Sales",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ReceiptNumber",
                table: "Sales",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SoldAt",
                table: "Sales",
                column: "SoldAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StockLocationId_SoldAt",
                table: "Sales",
                columns: new[] { "StockLocationId", "SoldAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAccounts_EmployeeId",
                table: "StaffAccounts",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentItems_ProductId",
                table: "StockAdjustmentItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentItems_StockAdjustmentId_ProductId",
                table: "StockAdjustmentItems",
                columns: new[] { "StockAdjustmentId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_RequestId",
                table: "StockAdjustments",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_StockLocationId_Date",
                table: "StockAdjustments",
                columns: new[] { "StockLocationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLocations_Code",
                table: "StockLocations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockLocations_NormalizedName",
                table: "StockLocations",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_StockTakeItems_ProductId",
                table: "StockTakeItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTakeItems_StockTakeId_ProductId",
                table: "StockTakeItems",
                columns: new[] { "StockTakeId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTakes_RequestId",
                table: "StockTakes",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTakes_StockLocationId_BusinessDate",
                table: "StockTakes",
                columns: new[] { "StockLocationId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductId",
                table: "StockTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_StockTransferId_ProductId",
                table: "StockTransferItems",
                columns: new[] { "StockTransferId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationLocationId_OccurredAt",
                table: "StockTransfers",
                columns: new[] { "DestinationLocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_OccurredAt",
                table: "StockTransfers",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_RequestId",
                table: "StockTransfers",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceLocationId_OccurredAt",
                table: "StockTransfers",
                columns: new[] { "SourceLocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBalanceEntries_PurchaseId",
                table: "SupplierBalanceEntries",
                column: "PurchaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBalanceEntries_RequestId",
                table: "SupplierBalanceEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBalanceEntries_SupplierBalanceId",
                table: "SupplierBalanceEntries",
                column: "SupplierBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBalanceEntries_SupplierPaymentId",
                table: "SupplierBalanceEntries",
                column: "SupplierPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBalances_SupplierId",
                table: "SupplierBalances",
                column: "SupplierId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_PurchaseId_PaidAt",
                table: "SupplierPayments",
                columns: new[] { "PurchaseId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_Reference",
                table: "SupplierPayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierBalanceId_PaidAt",
                table: "SupplierPayments",
                columns: new[] { "SupplierBalanceId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_LegacyId",
                table: "Suppliers",
                column: "LegacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_NormalizedName",
                table: "Suppliers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Technicians_LocationId",
                table: "Technicians",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Technicians_NormalizedName",
                table: "Technicians",
                column: "NormalizedName");

            migrationBuilder.AddForeignKey(
                name: "FK_CashDrawers_CashSessions_CurrentSessionId",
                table: "CashDrawers",
                column: "CurrentSessionId",
                principalTable: "CashSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashDrawers_StockLocations_StockLocationId",
                table: "CashDrawers");

            migrationBuilder.DropForeignKey(
                name: "FK_CashSessions_StockLocations_StockLocationId",
                table: "CashSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_CashDrawers_CashSessions_CurrentSessionId",
                table: "CashDrawers");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BankMovements");

            migrationBuilder.DropTable(
                name: "CustomerBalanceEntries");

            migrationBuilder.DropTable(
                name: "DayFinances");

            migrationBuilder.DropTable(
                name: "EditorDrafts");

            migrationBuilder.DropTable(
                name: "FinancialEntryBatches");

            migrationBuilder.DropTable(
                name: "ImportJobs");

            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "MobileServicingEntries");

            migrationBuilder.DropTable(
                name: "ProductAttributeOptions");

            migrationBuilder.DropTable(
                name: "ProductAttributeValues");

            migrationBuilder.DropTable(
                name: "ProductCostHistories");

            migrationBuilder.DropTable(
                name: "RecordChanges");

            migrationBuilder.DropTable(
                name: "SaleDraftPayments");

            migrationBuilder.DropTable(
                name: "StockTransferItems");

            migrationBuilder.DropTable(
                name: "SupplierBalanceEntries");

            migrationBuilder.DropTable(
                name: "WorkingDays");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "FinanceClosings");

            migrationBuilder.DropTable(
                name: "PurchaseItems");

            migrationBuilder.DropTable(
                name: "SaleItems");

            migrationBuilder.DropTable(
                name: "StockAdjustmentItems");

            migrationBuilder.DropTable(
                name: "StockTakeItems");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "Technicians");

            migrationBuilder.DropTable(
                name: "ProductAttributeDefinitions");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "StockAdjustments");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "StockTakes");

            migrationBuilder.DropTable(
                name: "CashTransfers");

            migrationBuilder.DropTable(
                name: "DebtEntries");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "PayrollEntries");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "LenderBalances");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "CustomerBalances");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "MonthlySalaries");

            migrationBuilder.DropTable(
                name: "StaffAccounts");

            migrationBuilder.DropTable(
                name: "Purchases");

            migrationBuilder.DropTable(
                name: "SupplierBalances");

            migrationBuilder.DropTable(
                name: "Lenders");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "StockLocations");

            migrationBuilder.DropTable(
                name: "CashSessions");

            migrationBuilder.DropTable(
                name: "CashDrawers");
        }
    }
}

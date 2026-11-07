using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationExpensesAndPaymentClearing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_CategoryDefinitionId",
                table: "Expenses");

            // 1. Rename old negative seeded categories so their names do not collide with new positive records
            migrationBuilder.Sql(@"UPDATE ""ExpenseCategories"" SET ""Name"" = ""Name"" || ' [old]' WHERE ""Id"" < 0;");

            // 2. Ensure positive DB categories exist for all standard types
            migrationBuilder.Sql(@"
INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Utilities', 'UTILITIES', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'UTILITIES' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Snacks / refreshments', 'SNACKS / REFRESHMENTS', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'SNACKS / REFRESHMENTS' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Transport', 'TRANSPORT', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'TRANSPORT' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Packaging', 'PACKAGING', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'PACKAGING' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Rent', 'RENT', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'RENT' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Maintenance', 'MAINTENANCE', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'MAINTENANCE' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Payment Processing Fees', 'PAYMENT PROCESSING FEES', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'PAYMENT PROCESSING FEES' AND ""Id"" > 0);

INSERT INTO ""ExpenseCategories"" (""Name"", ""NormalizedName"", ""IsArchived"")
SELECT 'Other', 'OTHER', false
WHERE NOT EXISTS (SELECT 1 FROM ""ExpenseCategories"" WHERE ""NormalizedName"" = 'OTHER' AND ""Id"" > 0);
");

            // 3. Map existing historical expenses pointing to negative IDs or null to the positive categories
            migrationBuilder.Sql(@"
UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'UTILITIES' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -1 OR (e.""CategoryDefinitionId"" IS NULL AND e.""Category"" = 1);

UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'SNACKS / REFRESHMENTS' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -2 OR (e.""CategoryDefinitionId"" IS NULL AND e.""Category"" = 2);

UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'TRANSPORT' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -3 OR (e.""CategoryDefinitionId"" IS NULL AND e.""Category"" = 3);

UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'PACKAGING' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -4 OR (e.""CategoryDefinitionId"" IS NULL AND e.""Category"" = 4);

UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'RENT' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -5 OR (e.""CategoryDefinitionId"" IS NULL AND e.""Category"" = 5);

UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'MAINTENANCE' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -6 OR (e.""CategoryDefinitionId"" IS NULL AND e.""Category"" = 6);

UPDATE ""Expenses"" e
SET ""CategoryDefinitionId"" = (SELECT c.""Id"" FROM ""ExpenseCategories"" c WHERE c.""NormalizedName"" = 'OTHER' AND c.""Id"" > 0 LIMIT 1)
WHERE e.""CategoryDefinitionId"" = -7 OR (e.""CategoryDefinitionId"" IS NULL) OR e.""CategoryDefinitionId"" < 0;
");

            // 4. Clean up the negative seeded rows from ExpenseCategories
            migrationBuilder.Sql(@"DELETE FROM ""ExpenseCategories"" WHERE ""Id"" < 0;");

            migrationBuilder.AddColumn<int>(
                name: "StockLocationId",
                table: "Expenses",
                type: "integer",
                nullable: true);

            // 4. Backfill existing expenses with location if available from cash drawer sessions or default store
            migrationBuilder.Sql(@"
UPDATE ""Expenses"" e
SET ""StockLocationId"" = (
    SELECT s.""StockLocationId""
    FROM ""CashMovements"" m
    JOIN ""CashSessions"" s ON m.""CashSessionId"" = s.""Id""
    WHERE m.""ExpenseId"" = e.""Id""
    LIMIT 1
)
WHERE e.""StockLocationId"" IS NULL;

UPDATE ""Expenses"" e
SET ""StockLocationId"" = (
    SELECT ""Id"" FROM ""StockLocations"" WHERE NOT ""IsDeleted"" ORDER BY ""Id"" LIMIT 1
)
WHERE e.""StockLocationId"" IS NULL AND EXISTS (SELECT 1 FROM ""StockLocations"");
");

            migrationBuilder.CreateTable(
                name: "PaymentPayouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankAccountId = table.Column<int>(type: "integer", nullable: false),
                    BankMovementId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseId = table.Column<int>(type: "integer", nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentPayouts_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentPayouts_BankMovements_BankMovementId",
                        column: x => x.BankMovementId,
                        principalTable: "BankMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentPayouts_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentPayouts_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentClearingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PayoutId = table.Column<int>(type: "integer", nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentClearingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentClearingEntries_PaymentPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "PaymentPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentClearingEntries_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentClearingEntries_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryDefinitionId_OccurredAt",
                table: "Expenses",
                columns: new[] { "CategoryDefinitionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_StockLocationId_OccurredAt",
                table: "Expenses",
                columns: new[] { "StockLocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentClearingEntries_PaymentId",
                table: "PaymentClearingEntries",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentClearingEntries_PayoutId",
                table: "PaymentClearingEntries",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentClearingEntries_RequestId",
                table: "PaymentClearingEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentClearingEntries_Status_StockLocationId_Method_Busine~",
                table: "PaymentClearingEntries",
                columns: new[] { "Status", "StockLocationId", "Method", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentClearingEntries_StockLocationId",
                table: "PaymentClearingEntries",
                column: "StockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_BankAccountId",
                table: "PaymentPayouts",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_BankMovementId",
                table: "PaymentPayouts",
                column: "BankMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_ExpenseId",
                table: "PaymentPayouts",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_RequestId",
                table: "PaymentPayouts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_StockLocationId_BusinessDate",
                table: "PaymentPayouts",
                columns: new[] { "StockLocationId", "BusinessDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_StockLocations_StockLocationId",
                table: "Expenses",
                column: "StockLocationId",
                principalTable: "StockLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_StockLocations_StockLocationId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "PaymentClearingEntries");

            migrationBuilder.DropTable(
                name: "PaymentPayouts");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CategoryDefinitionId_OccurredAt",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_StockLocationId_OccurredAt",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "StockLocationId",
                table: "Expenses");

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
                name: "IX_Expenses_CategoryDefinitionId",
                table: "Expenses",
                column: "CategoryDefinitionId");
        }
    }
}

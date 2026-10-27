using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSplitPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TenderedAmount",
                table: "Payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Earlier checkouts kept gross line totals and the discount only on Sales.
            // Bring those snapshots into the same net-line representation as new sales.
            migrationBuilder.Sql("""
                WITH weighted AS (
                    SELECT i."Id", i."SaleId", s."DiscountAmount" AS order_discount,
                           i."TotalAmount" * (s."DiscountAmount" / s."Subtotal") AS exact_discount
                    FROM "SaleItems" i
                    JOIN "Sales" s ON s."Id" = i."SaleId"
                    WHERE s."DiscountAmount" > 0 AND s."Subtotal" > 0
                      AND NOT EXISTS (SELECT 1 FROM "SaleItems" other
                                      WHERE other."SaleId" = s."Id" AND other."DiscountAmount" <> 0)
                ), rounded AS (
                    SELECT *, FLOOR(exact_discount * 100) / 100.0 AS base_discount
                    FROM weighted
                ), ranked AS (
                    SELECT *, SUM(base_discount) OVER (PARTITION BY "SaleId") AS allocated,
                           ROW_NUMBER() OVER (PARTITION BY "SaleId"
                               ORDER BY exact_discount - base_discount DESC, "Id") AS remainder_rank
                    FROM rounded
                ), allocations AS (
                    SELECT "Id", base_discount + CASE
                        WHEN remainder_rank <= ROUND((order_discount - allocated) * 100) THEN 0.01
                        ELSE 0 END AS discount
                    FROM ranked
                )
                UPDATE "SaleItems"
                SET "DiscountAmount" = (SELECT discount FROM allocations WHERE allocations."Id" = "SaleItems"."Id"),
                    "TotalAmount" = "TotalAmount" - (SELECT discount FROM allocations WHERE allocations."Id" = "SaleItems"."Id")
                WHERE "Id" IN (SELECT "Id" FROM allocations);
                """);

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

            migrationBuilder.CreateIndex(
                name: "IX_SaleDraftPayments_SaleId",
                table: "SaleDraftPayments",
                column: "SaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleDraftPayments");

            migrationBuilder.DropColumn(
                name: "TenderedAmount",
                table: "Payments");
        }
    }
}

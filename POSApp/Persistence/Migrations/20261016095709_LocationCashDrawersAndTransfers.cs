using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocationCashDrawersAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashDrawers_AspNetUsers_CashierId",
                table: "CashDrawers");

            migrationBuilder.DropIndex(
                name: "IX_CashDrawers_CashierId",
                table: "CashDrawers");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "CashSessions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "CashTransferId",
                table: "CashMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "CashDrawers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "CashDrawers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentCash",
                table: "CashDrawers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "CashDrawers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OpeningDate",
                table: "CashDrawers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockLocationId",
                table: "CashDrawers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "CashDrawers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedByUserId",
                table: "CashDrawers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashLocationId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_CashDrawerId_BusinessDate",
                table: "CashSessions",
                columns: new[] { "CashDrawerId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashTransferId",
                table: "CashMovements",
                column: "CashTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawers_StockLocationId",
                table: "CashDrawers",
                column: "StockLocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CashLocationId",
                table: "AspNetUsers",
                column: "CashLocationId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_StockLocations_CashLocationId",
                table: "AspNetUsers",
                column: "CashLocationId",
                principalTable: "StockLocations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashDrawers_StockLocations_StockLocationId",
                table: "CashDrawers",
                column: "StockLocationId",
                principalTable: "StockLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashMovements_CashTransfers_CashTransferId",
                table: "CashMovements",
                column: "CashTransferId",
                principalTable: "CashTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                UPDATE "CashSessions" s SET "BusinessDate" = COALESCE(
                    CASE WHEN s."ClosedAt" IS NULL THEN (SELECT "Date" FROM "WorkingDays" WHERE "Id" = 1) END,
                    (SELECT MAX(m."BusinessDate") FROM "CashMovements" m WHERE m."CashSessionId" = s."Id"),
                    (s."OpenedAt" AT TIME ZONE 'Asia/Dhaka')::date);

                UPDATE "CashDrawers" d SET
                    "CreatedByUserId" = d."CashierId",
                    "CreatedAt" = COALESCE((SELECT MIN(s."OpenedAt") FROM "CashSessions" s WHERE s."CashDrawerId" = d."Id"), now()),
                    "OpeningBalance" = COALESCE((SELECT s."OpeningCash" FROM "CashSessions" s WHERE s."CashDrawerId" = d."Id" ORDER BY s."Id" LIMIT 1), 0),
                    "OpeningDate" = (SELECT s."BusinessDate" FROM "CashSessions" s WHERE s."CashDrawerId" = d."Id" ORDER BY s."Id" LIMIT 1),
                    "CurrentCash" = COALESCE(
                        (SELECT s."ExpectedCash" FROM "CashSessions" s WHERE s."Id" = d."CurrentSessionId"),
                        (SELECT COALESCE(s."ActualClosingCash", s."ExpectedCash") FROM "CashSessions" s
                         WHERE s."CashDrawerId" = d."Id" ORDER BY s."Id" DESC LIMIT 1), 0);

                UPDATE "CashSessions" s SET "CreatedByUserId" = d."CashierId"
                FROM "CashDrawers" d WHERE s."CashDrawerId" = d."Id" AND s."CreatedByUserId" IS NULL;

                WITH candidates AS (
                    SELECT s."CashDrawerId", MIN(s."StockLocationId") AS location
                    FROM "CashSessions" s JOIN "StockLocations" l ON l."Id" = s."StockLocationId"
                    GROUP BY s."CashDrawerId"
                    HAVING COUNT(DISTINCT s."StockLocationId") = 1 AND BOOL_AND(l."CanSell" AND NOT l."IsDeleted")
                        AND NOT EXISTS (SELECT 1 FROM "CashSessions" other
                            WHERE other."CashDrawerId" = s."CashDrawerId" AND other."StockLocationId" IS NULL)
                )
                UPDATE "CashDrawers" d SET "StockLocationId" = c.location
                FROM candidates c WHERE c."CashDrawerId" = d."Id"
                  AND (SELECT COUNT(*) FROM candidates same WHERE same.location = c.location) = 1;

                UPDATE "AspNetUsers" u SET "CashLocationId" = d."StockLocationId"
                FROM "CashDrawers" d WHERE d."CashierId" = u."Id" AND d."StockLocationId" IS NOT NULL;

                -- Only draft/open days are recalculated. Closed summaries remain immutable.
                UPDATE "DayFinances" f SET "OpeningCash" =
                    COALESCE((SELECT SUM(d."CurrentCash") FROM "CashDrawers" d), 0)
                    - COALESCE((SELECT SUM(m."Amount") FROM "CashMovements" m WHERE m."BusinessDate" = f."Date"), 0)
                    - COALESCE((SELECT SUM(s."Difference") FROM "CashSessions" s WHERE s."BusinessDate" = f."Date" AND s."ClosedAt" IS NOT NULL), 0),
                    "ExpectedCash" = COALESCE((SELECT SUM(d."CurrentCash") FROM "CashDrawers" d), 0)
                    - COALESCE((SELECT SUM(s."Difference") FROM "CashSessions" s WHERE s."BusinessDate" = f."Date" AND s."ClosedAt" IS NOT NULL), 0)
                WHERE f."ClosedAt" IS NULL AND f."Date" = (SELECT "Date" FROM "WorkingDays" WHERE "Id" = 1);
                """);

            migrationBuilder.DropColumn(name: "CashierId", table: "CashDrawers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Location cash balances and paired transfer history cannot be converted back into cashier-owned drawers. Restore a pre-upgrade backup instead.");
        }
    }
}

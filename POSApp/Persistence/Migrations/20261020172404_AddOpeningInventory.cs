using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpeningInventoryItemId",
                table: "InventoryMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpeningInventories",
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
                    table.PrimaryKey("PK_OpeningInventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningInventories_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningInventoryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OpeningInventoryId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningInventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningInventoryItems_OpeningInventories_OpeningInventoryId",
                        column: x => x.OpeningInventoryId,
                        principalTable: "OpeningInventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningInventoryItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_OpeningInventoryItemId",
                table: "InventoryMovements",
                column: "OpeningInventoryItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningInventories_RequestId",
                table: "OpeningInventories",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningInventories_StockLocationId_Date",
                table: "OpeningInventories",
                columns: new[] { "StockLocationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningInventoryItems_OpeningInventoryId_ProductId",
                table: "OpeningInventoryItems",
                columns: new[] { "OpeningInventoryId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningInventoryItems_ProductId",
                table: "OpeningInventoryItems",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_OpeningInventoryItems_OpeningInventoryIt~",
                table: "InventoryMovements",
                column: "OpeningInventoryItemId",
                principalTable: "OpeningInventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_OpeningInventoryItems_OpeningInventoryIt~",
                table: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "OpeningInventoryItems");

            migrationBuilder.DropTable(
                name: "OpeningInventories");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_OpeningInventoryItemId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "OpeningInventoryItemId",
                table: "InventoryMovements");
        }
    }
}

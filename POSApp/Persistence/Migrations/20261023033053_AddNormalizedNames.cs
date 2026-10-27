using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var tables = new[]
            {
                "Categories",
                "Customers",
                "Employees",
                "ExpenseCategories",
                "Lenders",
                "Products",
                "StockLocations",
                "Suppliers"
            };

            foreach (var table in tables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "NormalizedName",
                    table: table,
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true);

                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"NormalizedName\" = UPPER(BTRIM(\"Name\"))");

                migrationBuilder.AlterColumn<string>(
                    name: "NormalizedName",
                    table: table,
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(256)",
                    oldMaxLength: 256,
                    oldNullable: true);

                migrationBuilder.CreateIndex(
                    name: $"IX_{table}_NormalizedName",
                    table: table,
                    column: "NormalizedName");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var tables = new[]
            {
                "Categories",
                "Customers",
                "Employees",
                "ExpenseCategories",
                "Lenders",
                "Products",
                "StockLocations",
                "Suppliers"
            };

            foreach (var table in tables)
            {
                migrationBuilder.DropIndex(
                    name: $"IX_{table}_NormalizedName",
                    table: table);

                migrationBuilder.DropColumn(
                    name: "NormalizedName",
                    table: table);
            }
        }
    }
}

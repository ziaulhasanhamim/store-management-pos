using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using POSApp.Persistence;

#nullable disable

namespace POSApp.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261022230000_StoreProductAttributeDisplayValues")]
public sealed class StoreProductAttributeDisplayValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DisplayValue",
            table: "ProductAttributeValues",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DisplayValue",
            table: "ProductAttributeValues");
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations;

public partial class AddSaleItemUnitDiscount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "DiscountAmount",
            table: "SaleItems",
            newName: "UnitDiscount");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitDiscount",
            table: "SaleItems",
            type: "numeric(38,20)",
            precision: 38,
            scale: 20,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.Sql("""
            UPDATE "SaleItems"
            SET "UnitDiscount" = CASE WHEN "Quantity" > 0
                THEN ROUND("UnitDiscount" / "Quantity", 20)
                ELSE 0 END;
            """);

        migrationBuilder.DropColumn(name: "TotalAmount", table: "SaleItems");
        migrationBuilder.AddColumn<decimal>(
            name: "TotalAmount",
            table: "SaleItems",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            computedColumnSql: """
                ("UnitPrice" - "UnitDiscount") * "Quantity"
                """,
            stored: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Snapshot the generated values before restoring an ordinary writable column.
        migrationBuilder.AddColumn<decimal>(
            name: "LegacyTotalAmount",
            table: "SaleItems",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.Sql("""
            UPDATE "SaleItems" SET "LegacyTotalAmount" = "TotalAmount";
            """);
        migrationBuilder.DropColumn(name: "TotalAmount", table: "SaleItems");
        migrationBuilder.RenameColumn(
            name: "LegacyTotalAmount",
            table: "SaleItems",
            newName: "TotalAmount");

        migrationBuilder.Sql("""
            UPDATE "SaleItems" SET "UnitDiscount" = ROUND("UnitDiscount" * "Quantity", 2);
            """);
        migrationBuilder.AlterColumn<decimal>(
            name: "UnitDiscount",
            table: "SaleItems",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(38,20)",
            oldPrecision: 38,
            oldScale: 20);
        migrationBuilder.RenameColumn(
            name: "UnitDiscount",
            table: "SaleItems",
            newName: "DiscountAmount");
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations;

public partial class RenameOpeningInventoryToStockAdjustments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => Rename(migrationBuilder, forward: true);

    protected override void Down(MigrationBuilder migrationBuilder) => Rename(migrationBuilder, forward: false);

    private static void Rename(MigrationBuilder migrationBuilder, bool forward)
    {
        var source = forward ? "OpeningInventories" : "StockAdjustments";
        var target = forward ? "StockAdjustments" : "OpeningInventories";
        var sourceItems = forward ? "OpeningInventoryItems" : "StockAdjustmentItems";
        var targetItems = forward ? "StockAdjustmentItems" : "OpeningInventoryItems";
        var sourceId = forward ? "OpeningInventoryId" : "StockAdjustmentId";
        var targetId = forward ? "StockAdjustmentId" : "OpeningInventoryId";
        var sourceItemId = forward ? "OpeningInventoryItemId" : "StockAdjustmentItemId";
        var targetItemId = forward ? "StockAdjustmentItemId" : "OpeningInventoryItemId";
        var sourceMovementKey = forward
            ? "FK_InventoryMovements_OpeningInventoryItems_OpeningInventoryIt~"
            : "FK_InventoryMovements_StockAdjustmentItems_StockAdjustmentItem~";
        var targetMovementKey = forward
            ? "FK_InventoryMovements_StockAdjustmentItems_StockAdjustmentItem~"
            : "FK_InventoryMovements_OpeningInventoryItems_OpeningInventoryIt~";

        migrationBuilder.DropForeignKey(sourceMovementKey, "InventoryMovements");
        migrationBuilder.DropForeignKey($"FK_{sourceItems}_{source}_{sourceId}", sourceItems);
        migrationBuilder.DropForeignKey($"FK_{sourceItems}_Products_ProductId", sourceItems);
        migrationBuilder.DropForeignKey($"FK_{source}_StockLocations_StockLocationId", source);
        migrationBuilder.DropPrimaryKey($"PK_{sourceItems}", sourceItems);
        migrationBuilder.DropPrimaryKey($"PK_{source}", source);

        // Rename existing tables/columns rather than dropping and recreating data.
        migrationBuilder.RenameTable(source, newName: target);
        migrationBuilder.RenameTable(sourceItems, newName: targetItems);
        migrationBuilder.RenameColumn(sourceId, targetItems, targetId);
        migrationBuilder.RenameColumn(sourceItemId, "InventoryMovements", targetItemId);
        migrationBuilder.RenameIndex($"IX_{source}_RequestId", table: target, newName: $"IX_{target}_RequestId");
        migrationBuilder.RenameIndex($"IX_{source}_StockLocationId_Date", table: target, newName: $"IX_{target}_StockLocationId_Date");
        migrationBuilder.RenameIndex($"IX_{sourceItems}_{sourceId}_ProductId", table: targetItems,
            newName: $"IX_{targetItems}_{targetId}_ProductId");
        migrationBuilder.RenameIndex($"IX_{sourceItems}_ProductId", table: targetItems, newName: $"IX_{targetItems}_ProductId");
        migrationBuilder.RenameIndex($"IX_InventoryMovements_{sourceItemId}", table: "InventoryMovements",
            newName: $"IX_InventoryMovements_{targetItemId}");

        migrationBuilder.AddPrimaryKey($"PK_{target}", target, "Id");
        migrationBuilder.AddPrimaryKey($"PK_{targetItems}", targetItems, "Id");
        migrationBuilder.AddForeignKey($"FK_{target}_StockLocations_StockLocationId", target, "StockLocationId",
            "StockLocations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey($"FK_{targetItems}_Products_ProductId", targetItems, "ProductId",
            "Products", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey($"FK_{targetItems}_{target}_{targetId}", targetItems, targetId,
            target, principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(targetMovementKey, "InventoryMovements", targetItemId,
            targetItems, principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
}

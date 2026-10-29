using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JASIntStoreManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicianLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "Technicians",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE ""Technicians""
                SET ""LocationId"" = (SELECT ""Id"" FROM ""StockLocations"" ORDER BY ""Id"" ASC LIMIT 1)
                WHERE ""LocationId"" = 0 AND EXISTS (SELECT 1 FROM ""StockLocations"");
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Technicians_LocationId",
                table: "Technicians",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Technicians_StockLocations_LocationId",
                table: "Technicians",
                column: "LocationId",
                principalTable: "StockLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Technicians_StockLocations_LocationId",
                table: "Technicians");

            migrationBuilder.DropIndex(
                name: "IX_Technicians_LocationId",
                table: "Technicians");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Technicians");
        }
    }
}

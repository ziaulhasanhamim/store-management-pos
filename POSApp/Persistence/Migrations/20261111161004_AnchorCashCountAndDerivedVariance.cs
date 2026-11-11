using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnchorCashCountAndDerivedVariance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AcceptedVariance",
                table: "CashCounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordedAt",
                table: "CashCounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql(@"
                UPDATE ""CashCounts""
                SET ""RecordedAt"" = ""CreatedAt"",
                    ""AcceptedVariance"" = CASE 
                        WHEN ""Status"" IN (3, 4) THEN ""CountedCash"" - ""ExpectedCash""
                        ELSE NULL 
                    END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedVariance",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "RecordedAt",
                table: "CashCounts");
        }
    }
}

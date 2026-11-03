using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyPartyBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Lenders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Lenders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OpenedAt",
                table: "LenderBalances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"LenderBalances\" SET \"OpenedAt\" = \"OpeningDate\"::timestamp with time zone WHERE \"OpenedAt\" IS NULL;");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "DebtEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Lenders");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Lenders");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "LenderBalances");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "DebtEntries");
        }
    }
}

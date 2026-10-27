using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenConcurrentPostingAndDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Purchases_SupplierId",
                table: "Purchases");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "SupplierPayments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "SupplierBalanceEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "Sales",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "RecordType",
                table: "RecordChanges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Purchases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "PayrollEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BroughtForward",
                table: "MonthlySalaries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CarriedForward",
                table: "MonthlySalaries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CarriedToMonth",
                table: "MonthlySalaries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Expenses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "CustomerBalanceEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "BankMovements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EditorDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SaveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId_SupplierInvoiceNumber",
                table: "Purchases",
                columns: new[] { "SupplierId", "SupplierInvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_EditorDrafts_UserId_Kind",
                table: "EditorDrafts",
                columns: new[] { "UserId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditorDrafts");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_SupplierId_SupplierInvoiceNumber",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "SupplierBalanceEntries");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "PayrollEntries");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BroughtForward",
                table: "MonthlySalaries");

            migrationBuilder.DropColumn(
                name: "CarriedForward",
                table: "MonthlySalaries");

            migrationBuilder.DropColumn(
                name: "CarriedToMonth",
                table: "MonthlySalaries");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "CustomerBalanceEntries");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "BankMovements");

            migrationBuilder.AlterColumn<string>(
                name: "RecordType",
                table: "RecordChanges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId",
                table: "Purchases",
                column: "SupplierId");
        }
    }
}

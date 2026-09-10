using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wida.Dal.Migrations;

public partial class AddInvoiceAdjustments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "ShippingAmount", table: "Invoices",
            type: "numeric(18,4)", precision: 18, scale: 4, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "DiscountAmount", table: "Invoices",
            type: "numeric(18,4)", precision: 18, scale: 4, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShippingAmount", table: "Invoices");
        migrationBuilder.DropColumn(name: "DiscountAmount", table: "Invoices");
    }
}

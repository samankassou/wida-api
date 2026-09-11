using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Wida.Dal.Migrations;
public partial class UserRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "Role", table: "Users", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>(name: "IsQuotaExempt", table: "ProcessingRuns", type: "boolean", nullable: false, defaultValue: false);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Role", table: "Users");
        migrationBuilder.DropColumn(name: "IsQuotaExempt", table: "ProcessingRuns");
    }
}

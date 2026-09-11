using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Wida.Dal.Migrations;

public partial class PublicTrial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "AnalysisPagesGranted", table: "Users", type: "integer", nullable: false, defaultValue: 4);
        migrationBuilder.AddColumn<int>(name: "AnalysisPagesUsed", table: "Users", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>(name: "CreditRequestedAt", table: "Users", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<int>(name: "PageCount", table: "Documents", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "ContentHash", table: "Documents", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<int>(name: "ReservedPages", table: "ProcessingRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "BudgetMonth", table: "ProcessingRuns", type: "character varying(7)", maxLength: 7, nullable: true);
        migrationBuilder.CreateTable(name: "AnalysisBudgets", columns: table => new
        {
            Id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
            PagesUsed = table.Column<int>(type: "integer", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_AnalysisBudgets", x => x.Id));
        migrationBuilder.DropIndex(name: "IX_Documents_OwnerUserId", table: "Documents");
        migrationBuilder.CreateIndex(name: "IX_Documents_OwnerUserId_ContentHash", table: "Documents", columns: new[] { "OwnerUserId", "ContentHash" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Documents_OwnerUserId_ContentHash", table: "Documents");
        migrationBuilder.CreateIndex(name: "IX_Documents_OwnerUserId", table: "Documents", column: "OwnerUserId");
        migrationBuilder.DropTable(name: "AnalysisBudgets");
        migrationBuilder.DropColumn(name: "AnalysisPagesGranted", table: "Users");
        migrationBuilder.DropColumn(name: "AnalysisPagesUsed", table: "Users");
        migrationBuilder.DropColumn(name: "CreditRequestedAt", table: "Users");
        migrationBuilder.DropColumn(name: "PageCount", table: "Documents");
        migrationBuilder.DropColumn(name: "ContentHash", table: "Documents");
        migrationBuilder.DropColumn(name: "ReservedPages", table: "ProcessingRuns");
        migrationBuilder.DropColumn(name: "BudgetMonth", table: "ProcessingRuns");
    }
}

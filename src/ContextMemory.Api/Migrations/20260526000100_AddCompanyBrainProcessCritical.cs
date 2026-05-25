using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Api.Migrations;

public partial class AddCompanyBrainProcessCritical : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCritical",
            table: "company_processes",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsCritical",
            table: "company_processes");
    }
}

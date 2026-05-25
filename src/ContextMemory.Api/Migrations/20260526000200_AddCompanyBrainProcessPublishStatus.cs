using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Api.Migrations;

public partial class AddCompanyBrainProcessPublishStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PublishStatus",
            table: "company_processes",
            type: "text",
            nullable: false,
            defaultValue: "Published");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PublishStatus",
            table: "company_processes");
    }
}

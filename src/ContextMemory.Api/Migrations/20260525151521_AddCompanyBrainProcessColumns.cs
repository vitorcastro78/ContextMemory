using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyBrainProcessColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCritical",
                table: "company_processes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublishStatus",
                table: "company_processes",
                type: "text",
                nullable: false,
                defaultValue: "Published");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishStatus",
                table: "company_processes");

            migrationBuilder.DropColumn(
                name: "IsCritical",
                table: "company_processes");
        }
    }
}

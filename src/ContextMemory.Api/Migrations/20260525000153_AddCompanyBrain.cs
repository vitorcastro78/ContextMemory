using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyBrain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    CompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    WebhookSecret = table.Column<string>(type: "text", nullable: true),
                    SkillsCacheJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.CompanyId);
                });

            migrationBuilder.CreateTable(
                name: "company_app_links",
                columns: table => new
                {
                    CompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_app_links", x => new { x.CompanyId, x.AppId });
                });

            migrationBuilder.CreateTable(
                name: "company_knowledge_sources",
                columns: table => new
                {
                    CompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_knowledge_sources", x => new { x.CompanyId, x.SourceId });
                });

            migrationBuilder.CreateTable(
                name: "company_processes",
                columns: table => new
                {
                    CompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    TriggersJson = table.Column<string>(type: "jsonb", nullable: false),
                    StepsJson = table.Column<string>(type: "jsonb", nullable: false),
                    GuardrailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceRef = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_processes", x => new { x.CompanyId, x.ProcessId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_app_links_AppId",
                table: "company_app_links",
                column: "AppId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "company_app_links");

            migrationBuilder.DropTable(
                name: "company_knowledge_sources");

            migrationBuilder.DropTable(
                name: "company_processes");
        }
    }
}

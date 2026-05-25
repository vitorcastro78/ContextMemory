using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnsureCompanyBrainProcessColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE company_processes
                    ADD COLUMN IF NOT EXISTS "IsCritical" boolean NOT NULL DEFAULT false;

                ALTER TABLE company_processes
                    ADD COLUMN IF NOT EXISTS "PublishStatus" text NOT NULL DEFAULT 'Published';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE company_processes DROP COLUMN IF EXISTS "PublishStatus";
                ALTER TABLE company_processes DROP COLUMN IF EXISTS "IsCritical";
                """);
        }
    }
}

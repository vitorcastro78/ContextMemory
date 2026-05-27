using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeLoopEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_loop_entries",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    MessagesJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvaluationJson = table.Column<string>(type: "jsonb", nullable: true),
                    IngestedPath = table.Column<string>(type: "text", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_loop_entries", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_loop_entries_AppId_Status",
                table: "knowledge_loop_entries",
                columns: new[] { "AppId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_loop_entries_CreatedAt",
                table: "knowledge_loop_entries",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_loop_entries");
        }
    }
}

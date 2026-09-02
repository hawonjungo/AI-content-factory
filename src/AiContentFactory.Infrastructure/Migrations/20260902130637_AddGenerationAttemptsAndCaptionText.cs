using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationAttemptsAndCaptionText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptionText",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "generation_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EstimatedCredits = table.Column<int>(type: "integer", nullable: false),
                    ActualCredits = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    AudioDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_attempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_generation_attempts_ContentProjectId",
                table: "generation_attempts",
                column: "ContentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_generation_attempts_ContentProjectId_SceneId_Kind",
                table: "generation_attempts",
                columns: new[] { "ContentProjectId", "SceneId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_generation_attempts_CreatedAt",
                table: "generation_attempts",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generation_attempts");

            migrationBuilder.DropColumn(
                name: "CaptionText",
                table: "scenes");
        }
    }
}

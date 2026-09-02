using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_usage_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Niche = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    AspectRatio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "qa_scores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hook = table.Column<double>(type: "double precision", nullable: false),
                    Story = table.Column<double>(type: "double precision", nullable: false),
                    Pacing = table.Column<double>(type: "double precision", nullable: false),
                    VisualQuality = table.Column<double>(type: "double precision", nullable: false),
                    AudioQuality = table.Column<double>(type: "double precision", nullable: false),
                    SubtitleQuality = table.Column<double>(type: "double precision", nullable: false),
                    Consistency = table.Column<double>(type: "double precision", nullable: false),
                    FactualAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    PlatformSuitability = table.Column<double>(type: "double precision", nullable: false),
                    Overall = table.Column<double>(type: "double precision", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qa_scores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hook = table.Column<string>(type: "text", nullable: false),
                    Introduction = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Escalation = table.Column<string>(type: "text", nullable: false),
                    Payoff = table.Column<string>(type: "text", nullable: false),
                    CallToAction = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "storyboards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storyboards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryboardId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneNumber = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Narration = table.Column<string>(type: "text", nullable: false),
                    VisualDescription = table.Column<string>(type: "text", nullable: false),
                    CameraDirection = table.Column<string>(type: "text", nullable: false),
                    VisualStyle = table.Column<string>(type: "text", nullable: true),
                    GenerationPrompt = table.Column<string>(type: "text", nullable: true),
                    NegativePrompt = table.Column<string>(type: "text", nullable: true),
                    VisualType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scenes_storyboards_StoryboardId",
                        column: x => x.StoryboardId,
                        principalTable: "storyboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_records_ContentProjectId",
                table: "ai_usage_records",
                column: "ContentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_records_CreatedAt",
                table: "ai_usage_records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_assets_ContentProjectId",
                table: "assets",
                column: "ContentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_SceneId",
                table: "assets",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_content_projects_Status",
                table: "content_projects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_qa_scores_ContentProjectId",
                table: "qa_scores",
                column: "ContentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_StoryboardId_SceneNumber",
                table: "scenes",
                columns: new[] { "StoryboardId", "SceneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scripts_ContentProjectId",
                table: "scripts",
                column: "ContentProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storyboards_ContentProjectId",
                table: "storyboards",
                column: "ContentProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage_records");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "content_projects");

            migrationBuilder.DropTable(
                name: "qa_scores");

            migrationBuilder.DropTable(
                name: "scenes");

            migrationBuilder.DropTable(
                name: "scripts");

            migrationBuilder.DropTable(
                name: "storyboards");
        }
    }
}

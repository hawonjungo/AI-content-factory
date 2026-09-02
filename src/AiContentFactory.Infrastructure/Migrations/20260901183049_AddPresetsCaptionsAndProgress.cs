using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPresetsCaptionsAndProgress : Migration
    {
        private const string DefaultCaptionsJson =
            """{"Bold":true,"Enabled":true,"Karaoke":true,"Position":0,"Animation":2,"Uppercase":false,"FontFamily":"DejaVu Sans","FontSizePt":54,"ShadowDepth":0,"OutlineColor":"#000000","OutlineWidth":3,"PrimaryColor":"#FFFFFF","HighlightColor":"#FFD400","MaxWordsPerCue":4,"MarginVerticalPx":220}""";

        private const string DefaultProgressJson =
            """{"Stage":"idle","Message":null,"UpdatedAt":"0001-01-01T00:00:00+00:00","TotalUnits":0,"CompletedUnits":0}""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptionPresetId",
                table: "content_projects",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            // EF scaffolded defaultValue: "" for these, which Postgres rejects -
            // an empty string is not valid jsonb.
            //
            // "{}" is valid but WRONG here: EF does not run CLR property
            // initializers when materializing an owned JSON type, so an empty
            // object comes back as FontFamily=null, FontSizePt=0, Enabled=false
            // - i.e. every pre-existing project silently loses its captions.
            // The defaults below are the exact serialized form EF writes
            // (PascalCase keys, enums as ints: Position Bottom=0,
            // Animation PopIn=2). They are a frozen snapshot of
            // CaptionSettings.Default() as of this migration and deliberately
            // do NOT track later changes to it - a migration records what
            // happened, and rewriting it would change history for databases
            // that already ran it.
            migrationBuilder.AddColumn<string>(
                name: "Captions",
                table: "content_projects",
                type: "jsonb",
                nullable: false,
                defaultValue: DefaultCaptionsJson);

            migrationBuilder.AddColumn<string>(
                name: "Progress",
                table: "content_projects",
                type: "jsonb",
                nullable: false,
                defaultValue: DefaultProgressJson);

            // Backfill for any database that already took an earlier form of
            // this migration and ended up with empty objects.
            migrationBuilder.Sql(
                $"UPDATE content_projects SET \"Captions\" = '{DefaultCaptionsJson}'::jsonb WHERE \"Captions\" = '{{}}'::jsonb;");
            migrationBuilder.Sql(
                $"UPDATE content_projects SET \"Progress\" = '{DefaultProgressJson}'::jsonb WHERE \"Progress\" = '{{}}'::jsonb;");

            migrationBuilder.AddColumn<string>(
                name: "StylePresetId",
                table: "content_projects",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "content_projects",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoicePresetId",
                table: "content_projects",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VideoGenerationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreditsUsed = table.Column<int>(type: "integer", nullable: false),
                    SceneId = table.Column<string>(type: "text", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoGenerationRecords", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoGenerationRecords");

            migrationBuilder.DropColumn(
                name: "CaptionPresetId",
                table: "content_projects");

            migrationBuilder.DropColumn(
                name: "Captions",
                table: "content_projects");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "content_projects");

            migrationBuilder.DropColumn(
                name: "StylePresetId",
                table: "content_projects");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "content_projects");

            migrationBuilder.DropColumn(
                name: "VoicePresetId",
                table: "content_projects");
        }
    }
}

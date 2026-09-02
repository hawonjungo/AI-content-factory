using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdeaConfigAndSceneAllocation : Migration
    {
        // EF scaffolds defaultValue: "" for a NOT NULL jsonb column, which
        // Postgres rejects (an empty string is not valid jsonb). These are the
        // serialized forms of ContentIdeaConfig.Default() / RenderValidationSummary.None()
        // - a frozen snapshot, deliberately NOT tracking later changes to those
        // types (a migration records what happened). An empty object is correct
        // for IdeaConfig because every field defaults to null / 0.
        private const string DefaultIdeaConfigJson = "{}";

        private const string DefaultRenderValidationJson =
            """{"Ok":false,"Summary":"","ErrorsText":"","WarningsText":"","DurationSeconds":0,"CheckedAt":null}""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiVideoPriority",
                table: "scenes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AllocationRationale",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelTier",
                table: "scenes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdeaConfig",
                table: "content_projects",
                type: "jsonb",
                nullable: false,
                defaultValue: DefaultIdeaConfigJson);

            migrationBuilder.AddColumn<string>(
                name: "LastRenderValidation",
                table: "content_projects",
                type: "jsonb",
                nullable: false,
                defaultValue: DefaultRenderValidationJson);

            // Backfill any row that a partially-applied earlier form of this
            // migration left with an invalid empty value.
            migrationBuilder.Sql(
                $"UPDATE content_projects SET \"IdeaConfig\" = '{DefaultIdeaConfigJson}'::jsonb WHERE \"IdeaConfig\"::text IN ('', '\"\"');");
            migrationBuilder.Sql(
                $"UPDATE content_projects SET \"LastRenderValidation\" = '{DefaultRenderValidationJson}'::jsonb WHERE \"LastRenderValidation\"::text IN ('', '\"\"');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiVideoPriority",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "AllocationRationale",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "ModelTier",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "IdeaConfig",
                table: "content_projects");

            migrationBuilder.DropColumn(
                name: "LastRenderValidation",
                table: "content_projects");
        }
    }
}

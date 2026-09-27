using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryCharacterLocationReferenceImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF scaffolded defaultValue: "" for these NOT NULL enum-as-string
            // columns; "" does not round-trip back to AssetReferenceStatus on
            // read, so existing rows must default to the actual enum name
            // ("Pending") instead.
            migrationBuilder.AddColumn<string>(
                name: "ReferenceImagePath",
                table: "story_locations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImagePrompt",
                table: "story_locations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImageProvider",
                table: "story_locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImageStatus",
                table: "story_locations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImagePath",
                table: "story_characters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImagePrompt",
                table: "story_characters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImageProvider",
                table: "story_characters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImageStatus",
                table: "story_characters",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceImagePath",
                table: "story_locations");

            migrationBuilder.DropColumn(
                name: "ReferenceImagePrompt",
                table: "story_locations");

            migrationBuilder.DropColumn(
                name: "ReferenceImageProvider",
                table: "story_locations");

            migrationBuilder.DropColumn(
                name: "ReferenceImageStatus",
                table: "story_locations");

            migrationBuilder.DropColumn(
                name: "ReferenceImagePath",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "ReferenceImagePrompt",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "ReferenceImageProvider",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "ReferenceImageStatus",
                table: "story_characters");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryCharacterCanonicalProfileAndPendingImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClothingAndAccessories",
                table: "story_characters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistinctiveFeatures",
                table: "story_characters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "story_characters",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<string>(
                name: "PendingReferenceImagePath",
                table: "story_characters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingReferenceImagePrompt",
                table: "story_characters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingReferenceImageProvider",
                table: "story_characters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingReferenceImageSource",
                table: "story_characters",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImageSource",
                table: "story_characters",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.AddColumn<string>(
                name: "Species",
                table: "story_characters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClothingAndAccessories",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "DistinctiveFeatures",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "PendingReferenceImagePath",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "PendingReferenceImagePrompt",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "PendingReferenceImageProvider",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "PendingReferenceImageSource",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "ReferenceImageSource",
                table: "story_characters");

            migrationBuilder.DropColumn(
                name: "Species",
                table: "story_characters");
        }
    }
}

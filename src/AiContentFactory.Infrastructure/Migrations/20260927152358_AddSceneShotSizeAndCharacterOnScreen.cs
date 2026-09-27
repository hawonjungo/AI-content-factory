using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneShotSizeAndCharacterOnScreen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CharacterOnScreen",
                table: "scenes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShotSize",
                table: "scenes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unspecified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharacterOnScreen",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "ShotSize",
                table: "scenes");
        }
    }
}

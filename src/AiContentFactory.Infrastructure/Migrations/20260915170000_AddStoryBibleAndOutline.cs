using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryBibleAndOutline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outline",
                table: "story_episodes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bible",
                table: "stories",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Outline",
                table: "story_episodes");

            migrationBuilder.DropColumn(
                name: "Bible",
                table: "stories");
        }
    }
}

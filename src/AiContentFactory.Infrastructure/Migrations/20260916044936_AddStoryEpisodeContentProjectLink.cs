using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryEpisodeContentProjectLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContentProjectId",
                table: "story_episodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_episodes_ContentProjectId",
                table: "story_episodes",
                column: "ContentProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_story_episodes_ContentProjectId",
                table: "story_episodes");

            migrationBuilder.DropColumn(
                name: "ContentProjectId",
                table: "story_episodes");
        }
    }
}

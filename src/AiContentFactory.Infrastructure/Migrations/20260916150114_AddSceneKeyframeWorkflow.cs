using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneKeyframeWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KeyframeAssetId",
                table: "scenes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyframeImagePrompt",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyframeStatus",
                table: "scenes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "MotionPrompt",
                table: "scenes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyframeAssetId",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "KeyframeImagePrompt",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "KeyframeStatus",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "MotionPrompt",
                table: "scenes");
        }
    }
}

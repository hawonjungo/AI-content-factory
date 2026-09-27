using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentProjectAudioMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add NOT NULL with a temporary default so existing rows are
            // backfilled to "Generated" - i.e. keep the current generate-a-new-
            // voice behaviour for every project that already exists. New
            // projects persist "Original" explicitly through EF, so the column
            // default is dropped immediately afterwards and never applies again.
            migrationBuilder.AddColumn<string>(
                name: "AudioMode",
                table: "content_projects",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.Sql("ALTER TABLE content_projects ALTER COLUMN \"AudioMode\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioMode",
                table: "content_projects");
        }
    }
}

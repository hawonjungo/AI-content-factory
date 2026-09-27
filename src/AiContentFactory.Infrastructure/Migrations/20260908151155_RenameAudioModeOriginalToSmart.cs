using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <summary>
    /// Data-only: the previous "Original" audio mode ("keep every clip's audio")
    /// is superseded by "Smart" (per-clip: keep original audio where present, TTS
    /// only for the gaps). Rename the stored value in place. Projects that were
    /// grandfathered to "Generated" are untouched.
    /// </summary>
    public partial class RenameAudioModeOriginalToSmart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE content_projects SET \"AudioMode\" = 'Smart' WHERE \"AudioMode\" = 'Original';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE content_projects SET \"AudioMode\" = 'Original' WHERE \"AudioMode\" = 'Smart';");
        }
    }
}

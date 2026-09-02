using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_references",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_references", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_asset_references_ContentProjectId_Type",
                table: "asset_references",
                columns: new[] { "ContentProjectId", "Type" });

            // Backfill so projects created before this step aren't stuck on the
            // new wizard gate. Each project that already had a ":reference"
            // Asset gets its newest one promoted to an Approved Character
            // reference, plus a Skipped Environment row. The old Asset rows are
            // left in place (files stay on disk) - just no longer read.
            migrationBuilder.Sql("""
                INSERT INTO asset_references ("Id", "ContentProjectId", "Type", "Status", "ImagePath", "Prompt", "Provider", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), latest."ContentProjectId", 'Character', 'Approved', latest."FilePath", NULL, 'backfill', now(), now()
                FROM (
                    SELECT DISTINCT ON (a."ContentProjectId") a."ContentProjectId", a."FilePath"
                    FROM assets a
                    WHERE a."SceneId" IS NULL
                      AND a."Type" = 'Image'
                      AND a."Provider" LIKE '%:reference'
                      AND a."FilePath" IS NOT NULL
                    ORDER BY a."ContentProjectId", a."CreatedAt" DESC
                ) latest;
                """);

            migrationBuilder.Sql("""
                INSERT INTO asset_references ("Id", "ContentProjectId", "Type", "Status", "ImagePath", "Prompt", "Provider", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), c."ContentProjectId", 'Environment', 'Skipped', NULL, NULL, 'backfill', now(), now()
                FROM asset_references c
                WHERE c."Type" = 'Character' AND c."Provider" = 'backfill';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_references");
        }
    }
}

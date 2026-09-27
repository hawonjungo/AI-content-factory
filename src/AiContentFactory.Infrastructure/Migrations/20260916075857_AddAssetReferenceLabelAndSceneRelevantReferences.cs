using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiContentFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetReferenceLabelAndSceneRelevantReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelevantReferenceLabelsText",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "asset_references",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_asset_references_ContentProjectId_Type_Label",
                table: "asset_references",
                columns: new[] { "ContentProjectId", "Type", "Label" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_asset_references_ContentProjectId_Type_Label",
                table: "asset_references");

            migrationBuilder.DropColumn(
                name: "RelevantReferenceLabelsText",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "asset_references");
        }
    }
}

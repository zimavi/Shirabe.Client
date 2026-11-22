using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shirabe.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddArtwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtworkFile",
                table: "Recordings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScannerVersion",
                table: "MediaSources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtworkFile",
                table: "Recordings");

            migrationBuilder.DropColumn(
                name: "ScannerVersion",
                table: "MediaSources");
        }
    }
}

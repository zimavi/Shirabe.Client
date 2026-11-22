using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shirabe.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Multiple = table.Column<bool>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Layer = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataEntries_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataDefinitions_Key",
                table: "MetadataDefinitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataEntries_Key",
                table: "MetadataEntries",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataEntries_RecordingId_Layer_Key",
                table: "MetadataEntries",
                columns: new[] { "RecordingId", "Layer", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataDefinitions");

            migrationBuilder.DropTable(
                name: "MetadataEntries");
        }
    }
}

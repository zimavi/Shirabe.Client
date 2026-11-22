using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shirabe.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SidecarStamp",
                table: "MediaSources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MetadataDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    Layer = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LoadedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataDocuments_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataDocuments_RecordingId_Origin_Layer",
                table: "MetadataDocuments",
                columns: new[] { "RecordingId", "Origin", "Layer" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataDocuments");

            migrationBuilder.DropColumn(
                name: "SidecarStamp",
                table: "MediaSources");
        }
    }
}

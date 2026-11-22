using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shirabe.Library.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Uri = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    IsMissing = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaSources_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtistCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistCredits", x => x.Id);
                    table.CheckConstraint("CK_ArtistCredit_exactly_one_owner", "(\"RecordingId\" IS NULL) <> (\"ReleaseId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ArtistCredits_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArtistCredits_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ArtistCredits_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReleaseTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseTracks_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseTracks_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtistCredits_ArtistId",
                table: "ArtistCredits",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistCredits_RecordingId",
                table: "ArtistCredits",
                column: "RecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistCredits_ReleaseId",
                table: "ArtistCredits",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaSources_RecordingId",
                table: "MediaSources",
                column: "RecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaSources_Uri",
                table: "MediaSources",
                column: "Uri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseTracks_RecordingId",
                table: "ReleaseTracks",
                column: "RecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseTracks_ReleaseId",
                table: "ReleaseTracks",
                column: "ReleaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistCredits");

            migrationBuilder.DropTable(
                name: "MediaSources");

            migrationBuilder.DropTable(
                name: "ReleaseTracks");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropTable(
                name: "Releases");
        }
    }
}

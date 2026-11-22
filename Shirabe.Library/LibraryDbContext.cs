using Microsoft.EntityFrameworkCore;
using Shirabe.Library.Model;

namespace Shirabe.Library;

public class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<Artist> Artists => Set<Artist>();

    public DbSet<Release> Releases => Set<Release>();

    public DbSet<Recording> Recordings => Set<Recording>();

    public DbSet<MediaSource> MediaSources => Set<MediaSource>();

    public DbSet<ReleaseTrack> ReleaseTracks => Set<ReleaseTrack>();

    public DbSet<ArtistCredit> ArtistCredits => Set<ArtistCredit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ids are generated in code (GUID v7), never by the database.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            entity.FindProperty("Id")!.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;

        modelBuilder.Entity<MediaSource>(e =>
        {
            e.HasIndex(s => s.Uri).IsUnique();
            e.HasOne(s => s.Recording).WithMany(r => r.Sources).HasForeignKey(s => s.RecordingId);
        });

        modelBuilder.Entity<ReleaseTrack>(e =>
        {
            e.HasOne(t => t.Release).WithMany(r => r.Tracks).HasForeignKey(t => t.ReleaseId);
            e.HasOne(t => t.Recording).WithMany(r => r.ReleaseTracks).HasForeignKey(t => t.RecordingId);
        });

        modelBuilder.Entity<ArtistCredit>(e =>
        {
            e.HasOne(c => c.Artist).WithMany().HasForeignKey(c => c.ArtistId);
            e.HasOne<Recording>().WithMany(r => r.Credits).HasForeignKey(c => c.RecordingId);
            e.HasOne<Release>().WithMany(r => r.Credits).HasForeignKey(c => c.ReleaseId);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_ArtistCredit_exactly_one_owner", "(\"RecordingId\" IS NULL) <> (\"ReleaseId\" IS NULL)"));
        });
    }
}

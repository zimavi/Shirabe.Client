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

    public DbSet<MetadataDefinition> MetadataDefinitions => Set<MetadataDefinition>();

    public DbSet<MetadataEntry> MetadataEntries => Set<MetadataEntry>();

    public DbSet<StoredMetadataDocument> MetadataDocuments => Set<StoredMetadataDocument>();

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

        modelBuilder.Entity<MetadataDefinition>(e =>
        {
            e.HasIndex(d => d.Key).IsUnique();
            e.Property(d => d.Type).HasConversion<string>(); // readable in the file, and safe if the enum is reordered
        });

        modelBuilder.Entity<MetadataEntry>(e =>
        {
            // Restrict, not cascade: personal data must never disappear as a side effect of deleting a recording.
            e.HasOne(m => m.Recording).WithMany().HasForeignKey(m => m.RecordingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.RecordingId, m.Layer, m.Key }).IsUnique();
            e.HasIndex(m => m.Key); // "which songs have this field", and the in-use checks for definitions
        });

        modelBuilder.Entity<StoredMetadataDocument>(e =>
        {
            // Restrict, as for fields: kept documents are not removed as a side effect of deleting a recording.
            e.HasOne(d => d.Recording).WithMany().HasForeignKey(d => d.RecordingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => new { d.RecordingId, d.Origin, d.Layer }).IsUnique();
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

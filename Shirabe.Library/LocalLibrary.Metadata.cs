using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;
using Shirabe.Library.Model;

namespace Shirabe.Library;

// The user's custom fields (personal layer of the Shirabe Metadata Format). These tables are never written by the
// scanner, so a rescan cannot touch them.
public sealed partial class LocalLibrary
{
    private const int MaxLabelLength = 100;
    private const int MaxDescriptionLength = 500;

    public async Task<IReadOnlyList<FieldDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        var rows = await db.MetadataDefinitions.AsNoTracking().OrderBy(d => d.Key).ToListAsync(cancellationToken);
        return [.. rows.Select(ToDefinition)];
    }

    public async Task SaveDefinitionAsync(FieldDefinition definition, CancellationToken cancellationToken = default)
    {
        RequireExtensionKey(definition.Key);
        if (!Enum.IsDefined(definition.Type)) throw new ArgumentException("Unknown field type.", nameof(definition));
        if (definition.Label is { Length: > MaxLabelLength } || definition.Description is { Length: > MaxDescriptionLength })
            throw new ArgumentException("The label or description is too long.", nameof(definition));

        await using var db = new LibraryDbContext(_options);
        var row = await db.MetadataDefinitions.SingleOrDefaultAsync(d => d.Key == definition.Key, cancellationToken);

        if (row is null)
        {
            row = new MetadataDefinition { Key = definition.Key };
            db.MetadataDefinitions.Add(row);
        }
        else if ((row.Type != definition.Type || row.Multiple != definition.Multiple)
                 && await db.MetadataEntries.AnyAsync(e => e.Key == definition.Key, cancellationToken))
        {
            throw new InvalidOperationException(
                $"\"{definition.Key}\" already has values, so its type and whether it is a list cannot change. Remove the values first.");
        }

        row.Type = definition.Type;
        row.Multiple = definition.Multiple;
        row.Label = string.IsNullOrWhiteSpace(definition.Label) ? null : definition.Label.Trim();
        row.Description = string.IsNullOrWhiteSpace(definition.Description) ? null : definition.Description.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveDefinitionAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        if (await db.MetadataEntries.AnyAsync(e => e.Key == key, cancellationToken))
            throw new InvalidOperationException($"\"{key}\" still has values on some songs, so it cannot be removed.");

        await db.MetadataDefinitions.Where(d => d.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FieldValue>> GetFieldsAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        var entries = await db.MetadataEntries.AsNoTracking()
            .Where(e => e.RecordingId == recordingId && e.Layer == MetadataLayers.Personal)
            .OrderBy(e => e.Key)
            .ToListAsync(cancellationToken);
        var definitions = (await db.MetadataDefinitions.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(d => d.Key);

        // An entry without a definition cannot be shown properly; none can exist yet, because SetFieldAsync requires one.
        return [.. entries.Where(e => definitions.ContainsKey(e.Key))
            .Select(e => new FieldValue(ToDefinition(definitions[e.Key]), JsonDocument.Parse(e.ValueJson).RootElement.Clone()))];
    }

    public async Task SetFieldAsync(Guid recordingId, string key, JsonElement value, CancellationToken cancellationToken = default)
    {
        RequireExtensionKey(key);

        await using var db = new LibraryDbContext(_options);
        var definition = await db.MetadataDefinitions.AsNoTracking().SingleOrDefaultAsync(d => d.Key == key, cancellationToken)
                         ?? throw new InvalidOperationException($"\"{key}\" has no definition yet. Define the field first.");

        // The personal layer: links to local files are allowed (standard, section 7.2).
        if (!ToDefinition(definition).Validate(value, allowFileLinks: true, out var error))
            throw new ArgumentException($"{key}: {error}", nameof(value));

        if (!await db.Recordings.AnyAsync(r => r.Id == recordingId, cancellationToken))
            throw new InvalidOperationException("That song is not in the library.");

        var json = JsonSerializer.Serialize(value); // compact, whatever whitespace the caller's JSON had
        var entry = await db.MetadataEntries.SingleOrDefaultAsync(
            e => e.RecordingId == recordingId && e.Layer == MetadataLayers.Personal && e.Key == key, cancellationToken);

        if (entry is null)
            db.MetadataEntries.Add(new MetadataEntry
            {
                RecordingId = recordingId, Layer = MetadataLayers.Personal, Key = key, ValueJson = json, UpdatedUtc = DateTime.UtcNow,
            });
        else
        {
            entry.ValueJson = json;
            entry.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveFieldAsync(Guid recordingId, string key, CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        await db.MetadataEntries
            .Where(e => e.RecordingId == recordingId && e.Layer == MetadataLayers.Personal && e.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileTag>> ReadFileTagsAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        // A file that is still there first; a missing one is read anyway so the person is told it is gone.
        var uri = await db.MediaSources.AsNoTracking()
                      .Where(s => s.RecordingId == recordingId)
                      .OrderBy(s => s.IsMissing).ThenBy(s => s.Uri)
                      .Select(s => s.Uri)
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new InvalidOperationException("That song is not in the library.");

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var location) || !location.IsFile)
            throw new InvalidOperationException("This song does not come from a file on this computer.");

        var path = location.LocalPath;
        if (!File.Exists(path)) throw new FileNotFoundException("The file was moved or deleted.", path);

        return await Task.Run(() => TagReader.ReadAll(path), cancellationToken);
    }

    private static FieldDefinition ToDefinition(MetadataDefinition row) =>
        new(row.Key, row.Type, row.Multiple, row.Label, row.Description);

    private static void RequireExtensionKey(string key)
    {
        if (!FieldKey.IsExtension(key))
            throw new ArgumentException(
                $"\"{key}\" is not a custom field name. Use namespace:name, for example local:mood (see the Shirabe Metadata Format).", nameof(key));
    }
}

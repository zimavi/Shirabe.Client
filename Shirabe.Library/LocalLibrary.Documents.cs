using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shirabe.Core.Metadata;
using Shirabe.Library.Model;
using Shirabe.Metadata;

namespace Shirabe.Library;

// Metadata documents of songs (the Shirabe Metadata Format): the ones found in or beside the audio file, replaced by every
// scan of that file, and the ones a person attached, which a scan never touches. The person's own fields stay in
// LocalLibrary.Metadata.cs; applying a personal document puts fields there, and only on request.
public sealed partial class LocalLibrary : IDocumentStore
{
    /// <summary>A document is a few kilobytes; more than this is not one (the standard suggests refusing embedded values over 1 MiB).</summary>
    private const int MaxDocumentSize = 1024 * 1024;

    private static readonly string[] LayerOrder = [MetadataLayers.Personal, "canonical", "community"];

    private sealed record FoundDocument(string Origin, MetadataDocument Document);

    public async Task<IReadOnlyList<StoredDocument>> GetDocumentsAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        var rows = await db.MetadataDocuments.AsNoTracking().Where(d => d.RecordingId == recordingId).ToListAsync(cancellationToken);

        static int Rank(string layer) => Array.IndexOf(LayerOrder, layer) is var i and >= 0 ? i : LayerOrder.Length;
        return [.. rows.OrderBy(r => Rank(r.Layer)).ThenBy(r => r.Origin, StringComparer.Ordinal)
            .Select(r => new StoredDocument(r.Origin, r.Layer, r.Json, r.Revision, r.ErrorCount, r.LoadedUtc))];
    }

    public async Task<IReadOnlyList<string>> RefreshFromFileAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var db = new LibraryDbContext(_options);
        var source = await db.MediaSources.Where(s => s.RecordingId == recordingId)
                         .OrderBy(s => s.IsMissing).ThenBy(s => s.Uri)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new InvalidOperationException("That song is not in the library.");

        if (!Uri.TryCreate(source.Uri, UriKind.Absolute, out var location) || !location.IsFile)
            return ["This song does not come from a file on this computer."];

        var path = location.LocalPath;
        if (!File.Exists(path)) return ["The file was moved or deleted."];

        string? embedded;
        try
        {
            embedded = await Task.Run(() => TagReader.Read(path).EmbeddedDocument, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return [$"The audio file could not be read: {ex.Message}"];
        }

        var (found, problems) = ReadFileDocuments(path, embedded, sidecar: true, embedded: true);
        var rows = ToRows(recordingId, found, problems);
        await ReplaceStoredAsync(db, recordingId, [DocumentOrigin.Sidecar, DocumentOrigin.Embedded], rows, null, cancellationToken);
        source.SidecarStamp = SidecarStampOf(path);
        await db.SaveChangesAsync(cancellationToken);
        return problems;
    }

    public async Task<string> WriteToFileAsync(
        Guid recordingId, MetadataDocument document, string origin, CancellationToken cancellationToken = default)
    {
        var errors = document.Validate().Where(p => p.IsError).ToList();
        if (errors.Count > 0)
            throw new ArgumentException($"The document does not follow the standard ({errors.Count} error{(errors.Count == 1 ? "" : "s")}), for example: {errors[0].Path}: {errors[0].Message}", nameof(document));
        if (origin is not (DocumentOrigin.Sidecar or DocumentOrigin.Embedded))
            throw new ArgumentException("Documents are written to the sidecar file or into the audio file.", nameof(origin));

        string path;
        await using (var db = new LibraryDbContext(_options))
        {
            var uri = await db.MediaSources.AsNoTracking().Where(s => s.RecordingId == recordingId)
                          .OrderBy(s => s.IsMissing).ThenBy(s => s.Uri).Select(s => s.Uri).FirstOrDefaultAsync(cancellationToken)
                      ?? throw new InvalidOperationException("That song is not in the library.");
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var location) || !location.IsFile)
                throw new InvalidOperationException("This song does not come from a file on this computer.");
            path = location.LocalPath;
        }

        if (!File.Exists(path)) throw new FileNotFoundException("The file was moved or deleted.", path);

        var message = origin == DocumentOrigin.Sidecar
            ? await WriteSidecarAsync(path, document, cancellationToken)
            : await WriteEmbeddedAsync(path, document, cancellationToken);

        await RefreshFromFileAsync(recordingId, cancellationToken);
        return message;
    }

    private static async Task<string> WriteSidecarAsync(string audioPath, MetadataDocument document, CancellationToken ct)
    {
        var sidecar = SidecarFile.PathFor(audioPath);
        var existing = File.Exists(sidecar) ? await File.ReadAllTextAsync(sidecar, ct) : null;
        if (!SidecarFile.TryMerge(existing, document, out var json, out var problem)) throw new InvalidOperationException(problem);

        var temp = sidecar + ".tmp"; // write beside, then swap, so a crash cannot leave half a file
        await File.WriteAllTextAsync(temp, json, ct);
        File.Move(temp, sidecar, overwrite: true);
        return $"Saved to {Path.GetFileName(sidecar)}, beside the audio file.";
    }

    /// <summary>
    /// Writes the tag into the audio file itself. The file is copied to the temporary folder first and read back afterwards:
    /// if the tag is not what was written, or anything else about the song (its title, artist, album, numbers, length,
    /// cover) changed, the copy is put back. The copy is deleted when all is well.
    /// </summary>
    private static async Task<string> WriteEmbeddedAsync(string audioPath, MetadataDocument document, CancellationToken ct)
    {
        FileTags before;
        try
        {
            before = await Task.Run(() => TagReader.Read(audioPath), ct);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"The audio file cannot be read, so nothing was written: {ex.Message}");
        }

        if (!SidecarFile.TryMerge(before.EmbeddedDocument, document, out var json, out var problem)) throw new InvalidOperationException(problem);
        if (json.Length > MaxDocumentSize) throw new InvalidOperationException("The document is too large to be written into the audio file.");

        var backup = Path.Combine(Path.GetTempPath(), $"shirabe-backup-{Guid.NewGuid():N}{Path.GetExtension(audioPath)}");
        File.Copy(audioPath, backup);
        File.SetAttributes(backup, FileAttributes.Normal); // a read-only audio file must not make its copy impossible to delete
        var keepBackup = false;
        try
        {
            await Task.Run(() => TagReader.WriteEmbeddedDocument(audioPath, json), ct);
            var after = await Task.Run(() => TagReader.Read(audioPath), ct);
            if (after.EmbeddedDocument != json || !SameSong(before, after))
                throw new IOException("Reading the file back after writing showed a difference.");
            return $"Written into the audio file {Path.GetFileName(audioPath)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try
            {
                // Only put the copy back if the file really changed: a write that failed at once (a read-only file) changed nothing.
                if (!SameBytes(audioPath, backup)) File.Copy(backup, audioPath, overwrite: true);
            }
            catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
            {
                keepBackup = true;
                throw new InvalidOperationException(
                    $"Writing failed ({ex.Message}) and the original could not be put back ({restore.Message}). A copy of the original is at {backup}.");
            }

            throw new InvalidOperationException($"The audio file was left as it was. Writing failed: {ex.Message}");
        }
        finally
        {
            if (!keepBackup)
            {
                try
                {
                    File.Delete(backup);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // a leftover temporary copy is harmless
                }
            }
        }
    }

    private static bool SameBytes(string a, string b)
    {
        using var first = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var second = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (first.Length != second.Length) return false;

        var one = new byte[81920];
        var two = new byte[81920];
        int read;
        while ((read = first.Read(one, 0, one.Length)) > 0)
        {
            second.ReadExactly(two, 0, read);
            if (!one.AsSpan(0, read).SequenceEqual(two.AsSpan(0, read))) return false;
        }

        return true;
    }

    /// <summary>The song itself, as far as tags show it, is unchanged: only the metadata tag may differ.</summary>
    private static bool SameSong(FileTags a, FileTags b) =>
        (a.Title, a.Artist, a.AlbumArtist, a.Album, a.Year, a.DiscNumber, a.TrackNumber)
        == (b.Title, b.Artist, b.AlbumArtist, b.Album, b.Year, b.DiscNumber, b.TrackNumber)
        && Math.Abs((a.Duration ?? TimeSpan.Zero).TotalMilliseconds - (b.Duration ?? TimeSpan.Zero).TotalMilliseconds) < 5
        && (a.Cover ?? []).AsSpan().SequenceEqual(b.Cover ?? []);

    public async Task<IReadOnlyList<ApplyOutcome>> ApplyAsync(
        Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken cancellationToken = default)
    {
        var errors = document.Validate().Where(p => p.IsError).ToList();
        if (errors.Count > 0)
            throw new ArgumentException($"The document does not follow the standard ({errors.Count} error{(errors.Count == 1 ? "" : "s")}), for example: {errors[0].Path}: {errors[0].Message}", nameof(document));

        await using (var db = new LibraryDbContext(_options))
            if (!await db.Recordings.AnyAsync(r => r.Id == recordingId, cancellationToken))
                throw new InvalidOperationException("That song is not in the library.");

        return document.Layer == MetadataLayers.Personal
            ? await ApplyPersonalAsync(recordingId, document, dryRun, cancellationToken)
            : await KeepDocumentAsync(recordingId, document, dryRun, cancellationToken);
    }

    private async Task<IReadOnlyList<ApplyOutcome>> ApplyPersonalAsync(
        Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken ct)
    {
        var outcomes = new List<ApplyOutcome>();
        var known = (await GetDefinitionsAsync(ct)).ToDictionary(d => d.Key);
        var current = (await GetFieldsAsync(recordingId, ct)).ToDictionary(f => f.Definition.Key);
        var plan = new List<(FieldDefinition Definition, bool IsNew, JsonElement Value)>();

        foreach (var (key, value) in document.Fields)
        {
            if (!key.Contains(':'))
            {
                outcomes.Add(Skipped($"\"{key}\" is a core field of the standard. Your own fields cannot hold those yet, so it is left in the document."));
                continue;
            }

            var written = document.Definitions.TryGetValue(key, out var d) ? d : FieldDefinition.Infer(key, value);
            if (written is null)
            {
                outcomes.Add(Skipped($"\"{key}\" has no definition, and its value is not text, a number, yes/no, a link, or a list of one of those."));
                continue;
            }

            var isNew = !known.TryGetValue(key, out var mine);
            var definition = isNew ? written : mine!;
            if (!isNew && (mine!.Type != written.Type || mine.Multiple != written.Multiple))
            {
                outcomes.Add(Skipped($"\"{definition.DisplayName}\" is {Kind(mine)} here, but {Kind(written)} in the document."));
                continue;
            }

            if (!definition.Validate(value, allowFileLinks: true, out var error))
            {
                outcomes.Add(Skipped($"\"{definition.DisplayName}\": {error}"));
                continue;
            }

            if (current.TryGetValue(key, out var before))
            {
                if (JsonSerializer.Serialize(before.Value) == JsonSerializer.Serialize(value))
                {
                    outcomes.Add(Skipped($"\"{definition.DisplayName}\" already has this value."));
                    continue;
                }

                outcomes.Add(new ApplyOutcome(ApplyKind.Set, $"Replace \"{definition.DisplayName}\": {Describe(before.Value)} becomes {Describe(value)}."));
            }
            else
            {
                outcomes.Add(new ApplyOutcome(ApplyKind.Set, $"Set \"{definition.DisplayName}\" to {Describe(value)}{(isNew ? " (a new field)" : "")}."));
            }

            plan.Add((definition, isNew, value));
        }

        if (!dryRun)
            foreach (var (definition, isNew, value) in plan)
            {
                if (isNew) await SaveDefinitionAsync(definition, ct);
                await SetFieldAsync(recordingId, definition.Key, value, ct);
            }

        return outcomes;
    }

    private async Task<IReadOnlyList<ApplyOutcome>> KeepDocumentAsync(
        Guid recordingId, MetadataDocument document, bool dryRun, CancellationToken ct)
    {
        var layer = document.Layer!; // validated: one of the three
        await using var db = new LibraryDbContext(_options);
        var existing = await db.MetadataDocuments.SingleOrDefaultAsync(
            d => d.RecordingId == recordingId && d.Origin == DocumentOrigin.Applied && d.Layer == layer, ct);

        var from = document.SourceName is { } name ? $"{document.SourceKind}, {name}" : document.SourceKind;
        var text = $"Keep this {layer} document ({from}, revision {document.Revision}) with the song. "
                   + "It is shown in the song's properties; its fields do not change the Songs list yet."
                   + (existing is null ? "" : $" It replaces the one kept before (revision {existing.Revision}).");
        if (dryRun) return [new ApplyOutcome(ApplyKind.Keep, text)];

        if (existing is null)
        {
            existing = new StoredMetadataDocument { RecordingId = recordingId, Origin = DocumentOrigin.Applied, Layer = layer, Json = "" };
            db.MetadataDocuments.Add(existing);
        }

        existing.Json = document.ToJson();
        existing.Revision = document.Revision;
        existing.ErrorCount = 0;
        existing.LoadedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return [new ApplyOutcome(ApplyKind.Keep, text)];
    }

    // ---- reading documents from files (used by scans and by RefreshFromFileAsync) ----

    /// <summary>Size and modification time of the sidecar, or null when there is none. A change means: read it again.</summary>
    private static string? SidecarStampOf(string audioPath)
    {
        try
        {
            var info = new FileInfo(SidecarFile.PathFor(audioPath));
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the sidecar and/or the embedded tag of an audio file. A document that cannot be read never fails the caller:
    /// what went wrong comes back as sentences.
    /// </summary>
    private static (List<FoundDocument> Found, List<string> Problems) ReadFileDocuments(
        string audioPath, string? embeddedJson, bool sidecar, bool embedded)
    {
        var found = new List<FoundDocument>();
        var problems = new List<string>();

        void Parse(string json, string origin, string what)
        {
            if (!MetadataDocument.TryParseFile(json, out var documents, out var error)) problems.Add($"{what} is not a metadata document: {error}");
            else found.AddRange(documents.Select(d => new FoundDocument(origin, d)));
        }

        if (sidecar)
        {
            var path = SidecarFile.PathFor(audioPath);
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxDocumentSize) problems.Add("The sidecar file is far larger than a metadata document, so it was not read.");
                else if (info.Exists) Parse(File.ReadAllText(path, Encoding.UTF8), DocumentOrigin.Sidecar, "The sidecar file");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"The sidecar file could not be read: {ex.Message}");
            }
        }

        if (embedded && embeddedJson is not null)
        {
            if (embeddedJson.Length > MaxDocumentSize) problems.Add("The metadata tag in the audio file is far larger than a metadata document, so it was not read.");
            else Parse(embeddedJson, DocumentOrigin.Embedded, "The metadata tag in the audio file");
        }

        return (found, problems);
    }

    /// <summary>One row per origin and layer. When a file holds two documents of the same layer the newest is kept, and that is reported.</summary>
    private static List<StoredMetadataDocument> ToRows(Guid recordingId, IEnumerable<FoundDocument> found, List<string> problems)
    {
        var rows = new Dictionary<(string Origin, string Layer), StoredMetadataDocument>();
        foreach (var (origin, document) in found)
        {
            var layer = document.Layer is { } l && StandardVocabulary.Layers.Contains(l) ? l : "invalid";
            var row = new StoredMetadataDocument
            {
                RecordingId = recordingId,
                Origin = origin,
                Layer = layer,
                Json = document.ToJson(),
                Revision = document.Revision,
                ErrorCount = document.Validate().Count(p => p.IsError),
                LoadedUtc = DateTime.UtcNow,
            };

            if (rows.TryGetValue((origin, layer), out var earlier))
            {
                problems.Add($"The {origin} holds more than one {layer} document; only the newest is kept.");
                if ((earlier.Revision ?? 0) > (row.Revision ?? 0)) continue;
            }

            rows[(origin, layer)] = row;
        }

        return [.. rows.Values];
    }

    /// <summary>
    /// Replaces what was stored for these origins. <paramref name="withDocuments"/> (the recordings known to have stored
    /// documents) saves a delete per file during a scan; null always deletes.
    /// </summary>
    private static async Task ReplaceStoredAsync(
        LibraryDbContext db, Guid recordingId, string[] origins, List<StoredMetadataDocument> rows, ISet<Guid>? withDocuments, CancellationToken ct)
    {
        if (withDocuments is null || withDocuments.Contains(recordingId))
            await db.MetadataDocuments.Where(d => d.RecordingId == recordingId && origins.Contains(d.Origin)).ExecuteDeleteAsync(ct);

        foreach (var row in rows) db.MetadataDocuments.Add(row);
        if (rows.Count > 0) withDocuments?.Add(recordingId);
    }

    private static ApplyOutcome Skipped(string text) => new(ApplyKind.Skip, "Skipped " + text);

    private static string Kind(FieldDefinition d) => (d.Multiple ? "a list of " : "a ") + d.TypeName;

    /// <summary>A value in a few words, for a sentence.</summary>
    private static string Describe(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => $"\"{value.GetString()}\"",
            JsonValueKind.True => "yes",
            JsonValueKind.False => "no",
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(Describe)),
            JsonValueKind.Object when value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String => url.GetString()!,
            _ => value.GetRawText(),
        };
        return text.Length > 80 ? text[..80] + "..." : text;
    }
}

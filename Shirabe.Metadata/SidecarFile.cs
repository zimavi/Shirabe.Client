namespace Shirabe.Metadata;

/// <summary>
/// The sidecar file of the standard (section 13.2): <c>song.flac</c> has <c>song.flac.shirabe.json</c> beside it,
/// holding one document or a list of documents, for instance one per layer.
/// </summary>
public static class SidecarFile
{
    public const string Extension = ".shirabe.json";

    /// <summary>The audio file's extension stays in the name, so <c>song.flac</c> and <c>song.mp3</c> do not share a sidecar.</summary>
    public static string PathFor(string audioPath) => audioPath + Extension;

    /// <summary>
    /// The text of a sidecar (or of an embedded tag, which holds the same) after <paramref name="document"/> is put into it. Documents of other layers already in the file
    /// are kept exactly (an artist's canonical document must survive a person saving their own notes); a document of the
    /// same layer is replaced, and its <c>revision</c> goes up by one. This changes <paramref name="document"/> to say so.
    /// </summary>
    /// <param name="existingJson">What the sidecar holds now, or null when there is none.</param>
    /// <param name="error">Why nothing should be written: the existing file cannot be read, so it must be left alone.</param>
    public static bool TryMerge(string? existingJson, MetadataDocument document, out string json, out string? error)
    {
        json = "";
        error = null;

        var documents = new List<MetadataDocument>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            if (!MetadataDocument.TryParseFile(existingJson, out var existing, out var parseError))
            {
                error = $"The metadata that is already there cannot be read ({parseError}), so it was left alone.";
                return false;
            }

            documents.AddRange(existing);
        }

        var index = documents.FindIndex(d => d.Layer == document.Layer);
        if (index >= 0)
        {
            var old = documents[index];
            var now = document.Created;
            document.Root["revision"] = (old.Revision ?? 0) + 1;
            if (document.Root["source"] is System.Text.Json.Nodes.JsonObject source)
            {
                source["created"] = old.Created ?? now; // the document is as old as its first version
                source["updated"] = now;
            }

            documents[index] = document;
        }
        else
        {
            documents.Add(document);
        }

        json = MetadataDocument.ToJson(documents);
        return true;
    }
}

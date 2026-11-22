namespace Shirabe.Core.Metadata;

/// <summary>One line of what an audio file itself contains.</summary>
/// <param name="Group"><c>"Tags"</c> for what is written in the file's tags, <c>"Audio"</c> for technical facts about the audio.</param>
/// <param name="Name">Readable for the common tags ("Album artist"), the raw name for the rest (<c>REPLAYGAIN_TRACK_GAIN</c>).</param>
public sealed record FileTag(string Group, string Name, string Value)
{
    public const string TagsGroup = "Tags";
    public const string AudioGroup = "Audio";
}

/// <summary>Reads what the audio file of a recording contains, as it is, without changing anything.</summary>
public interface IFileTags
{
    /// <summary>
    /// Everything in the file's tags that has a value (empty tags are left out), then the audio facts.
    /// Failures: <see cref="InvalidOperationException"/> when the song is not in the library or is not a local file,
    /// <see cref="FileNotFoundException"/> when the file was moved or deleted, <see cref="InvalidDataException"/> when
    /// it can no longer be read as audio.
    /// </summary>
    Task<IReadOnlyList<FileTag>> ReadFileTagsAsync(Guid recordingId, CancellationToken cancellationToken = default);
}

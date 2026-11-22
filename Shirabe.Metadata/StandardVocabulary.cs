namespace Shirabe.Metadata;

/// <summary>The fixed names of the Shirabe Metadata Format, draft 0.1 (wiki/Metadata-Standard.md).</summary>
public static class StandardVocabulary
{
    public static IReadOnlySet<string> Layers { get; } = new HashSet<string> { "canonical", "community", "personal" };

    public static IReadOnlySet<string> SourceKinds { get; } = new HashSet<string> { "user", "artist", "instance", "import" };

    public static IReadOnlySet<string> SubjectTypes { get; } = new HashSet<string> { "recording" };

    /// <summary>Core fields of a recording (section 5).</summary>
    public static IReadOnlySet<string> CoreFields { get; } =
        new HashSet<string> { "title", "credits", "releases", "artwork", "links", "description", "presentation" };

    /// <summary>Credit roles (section 6). Others must be namespaced.</summary>
    public static IReadOnlySet<string> CoreRoles { get; } = new HashSet<string>
    {
        "artist", "featured", "album-artist", "vocals", "guitar", "bass", "drums", "keyboards", "composer", "lyricist",
        "arranger", "producer", "recording-engineer", "mixing-engineer", "mastering-engineer",
    };

    /// <summary>Artwork roles (section 7). Others must be namespaced.</summary>
    public static IReadOnlySet<string> LinkRoles { get; } =
        new HashSet<string> { "front", "back", "booklet", "disc", "artist", "banner", "other" };

    /// <summary>Identifier schemes (section 3). Others must be namespaced.</summary>
    public static IReadOnlySet<string> IdentifierSchemes { get; } =
        new HashSet<string> { "isrc", "musicbrainz.recording", "musicbrainz.release", "musicbrainz.artist" };

    /// <summary>Timeline tracks (section 8.2). Others must be namespaced.</summary>
    public static IReadOnlySet<string> Tracks { get; } =
        new HashSet<string> { "title", "artwork", "performers", "highlight", "description", "note" };

    public static IReadOnlySet<string> Transitions { get; } = new HashSet<string> { "cut", "fade", "slide" };

    /// <summary>The newest format version this code knows.</summary>
    public const string Version = "0.1";
}

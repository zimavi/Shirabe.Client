using Shirabe.Client.ViewModels;
using Shirabe.Metadata;

namespace Shirabe.Client.Studio;

/// <summary>One document of an opened file: what it is, whether it follows the standard, and its JSON as it will be written.</summary>
public sealed class StudioDocumentViewModel : ViewModelBase
{
    private readonly bool _canApply;

    /// <param name="canApply">Studio has a library to apply to.</param>
    public StudioDocumentViewModel(MetadataDocument document, bool canApply = false)
    {
        _canApply = canApply;
        Document = document;
        Problems = document.Validate();
        Json = document.ToJson();
    }

    public MetadataDocument Document { get; }

    public IReadOnlyList<DocumentProblem> Problems { get; }

    public string Json { get; }

    public int ErrorCount => Problems.Count(p => p.Severity == ProblemSeverity.Error);

    public int WarningCount => Problems.Count(p => p.Severity == ProblemSeverity.Warning);

    public bool IsValid => ErrorCount == 0;

    /// <summary>Only a document that follows the standard can be applied to a song, and only when there is a library.</summary>
    public bool CanApply => _canApply && IsValid;

    public bool HasProblems => Problems.Count > 0;

    public string Heading => Document.Title ?? Document.SubjectId ?? "Document";

    /// <summary>"Follows the standard", or what is wrong, in a few words.</summary>
    public string Status =>
        (ErrorCount, WarningCount) switch
        {
            (0, 0) => "Follows the standard",
            (0, var w) => $"Follows the standard, {Count(w, "warning")}",
            (var e, 0) => $"Does not follow the standard: {Count(e, "error")}",
            (var e, var w) => $"Does not follow the standard: {Count(e, "error")}, {Count(w, "warning")}",
        };

    /// <summary>The main facts, one per line, skipping what the document does not say.</summary>
    public IReadOnlyList<string> Facts
    {
        get
        {
            var facts = new List<string>();
            if (Document.Layer is { } layer) facts.Add($"Layer: {layer}");
            if (Document.SourceKind is { } kind) facts.Add($"From: {kind}{(Document.SourceName is { } name ? $", {name}" : "")}");
            if (Document.Created is { } created) facts.Add($"Created: {created}");
            if (Document.Revision is { } revision) facts.Add($"Revision: {revision}");
            if (Document.SubjectId is { } subject) facts.Add($"About: {Document.SubjectType ?? "subject"} {subject}");
            if (Document.Version is { } version) facts.Add($"Format version: {version}");
            if (Document.FieldNames.Count > 0) facts.Add($"Fields: {string.Join(", ", Document.FieldNames)}");
            if (Document.HasPresentation) facts.Add("Has a presentation timeline");
            return facts;
        }
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}

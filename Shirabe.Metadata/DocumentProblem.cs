namespace Shirabe.Metadata;

public enum ProblemSeverity
{
    /// <summary>The document breaks a rule of the standard.</summary>
    Error,

    /// <summary>Allowed, or readable anyway, but probably not what the author meant.</summary>
    Warning,
}

/// <param name="Path">Where, in the words of the document: <c>fields.credits[1].role</c>.</param>
public sealed record DocumentProblem(ProblemSeverity Severity, string Path, string Message)
{
    public bool IsError => Severity == ProblemSeverity.Error;

    public override string ToString() => $"{Severity}: {Path}: {Message}";
}

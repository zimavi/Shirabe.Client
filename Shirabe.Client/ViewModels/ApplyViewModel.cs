using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Core.Library;
using Shirabe.Core.Metadata;
using Shirabe.Metadata;

namespace Shirabe.Client.ViewModels;

/// <param name="Label">"Change", "Attach" or "Skipped": what kind of thing the sentence is about, without repeating its verb.</param>
public sealed record ApplyOutcomeRow(string Label, string Text, bool IsSkip);

/// <summary>
/// Applying one metadata document to one song: first what would happen, written out in sentences, then the person
/// decides. Used wherever a document can be applied (Studio, the properties dialog). Nothing is written until
/// <see cref="ApplyCommand"/>.
/// </summary>
public partial class ApplyViewModel : ViewModelBase
{
    private readonly IDocumentStore _store;
    private readonly Func<Task>? _applied;

    /// <param name="applied">Called after the document was applied, so the caller can show the new state.</param>
    public ApplyViewModel(IDocumentStore store, TrackSummary song, MetadataDocument document, Func<Task>? applied = null)
    {
        _store = store;
        _applied = applied;
        Song = song;
        Document = document;
    }

    public TrackSummary Song { get; }

    public MetadataDocument Document { get; }

    public string Heading => $"Apply to \"{Song.Title}\"";

    [ObservableProperty]
    public partial IReadOnlyList<ApplyOutcomeRow> Rows { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool CanApply { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>What applying did, once it has.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string? Result { get; set; }

    public bool HasResult => Result is not null;

    /// <summary>Raised when the panel should go away.</summary>
    public event EventHandler? Closed;

    /// <summary>Works out what applying would do. Nothing is changed.</summary>
    public async Task LoadAsync()
    {
        Error = null;
        try
        {
            var outcomes = await _store.ApplyAsync(Song.RecordingId, Document, dryRun: true);
            Rows = ToRows(outcomes);
            CanApply = outcomes.Any(o => o.Kind != ApplyKind.Skip);
            if (!CanApply) Result = "There is nothing to apply: every part of the document is skipped.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Rows = [];
            CanApply = false;
            Error = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyNow))]
    private async Task ApplyAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            var outcomes = await _store.ApplyAsync(Song.RecordingId, Document, dryRun: false);
            Rows = ToRows(outcomes);
            CanApply = false; // done: applying again would only report that nothing is left to do
            Result = Summarise(outcomes);
            if (_applied is not null) await _applied();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApplyNow() => CanApply && !IsBusy;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    private static IReadOnlyList<ApplyOutcomeRow> ToRows(IReadOnlyList<ApplyOutcome> outcomes) =>
        [.. outcomes.Select(o => new ApplyOutcomeRow(
            o.Kind switch { ApplyKind.Set => "Change", ApplyKind.Keep => "Attach", _ => "Skipped" },
            o.Kind == ApplyKind.Skip ? o.Text.Replace("Skipped ", "", StringComparison.Ordinal) : o.Text,
            o.Kind == ApplyKind.Skip))];

    private static string Summarise(IReadOnlyList<ApplyOutcome> outcomes)
    {
        var parts = new List<string>();
        var sets = outcomes.Count(o => o.Kind == ApplyKind.Set);
        if (sets > 0) parts.Add(sets == 1 ? "1 field set" : $"{sets} fields set");
        if (outcomes.Any(o => o.Kind == ApplyKind.Keep)) parts.Add("the document is kept with the song");
        var skipped = outcomes.Count(o => o.Kind == ApplyKind.Skip);
        if (skipped > 0) parts.Add($"{skipped} skipped");
        return "Done: " + string.Join(", ", parts) + ".";
    }
}

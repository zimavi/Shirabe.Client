using Shirabe.Client.Services;
using Shirabe.Client.Studio;
using Shirabe.Client.ViewModels;
using Xunit;

namespace Shirabe.Tests;

public sealed class StudioViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"shirabe-studio-{Guid.NewGuid():N}");
    private readonly FakeFilePicker _picker = new();

    public StudioViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Valid = """
        { "format": "shirabe.metadata", "version": "0.1",
          "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
          "layer": "personal", "source": { "kind": "user", "created": "2026-11-22T18:04:00Z" }, "revision": 1,
          "fields": { "title": "My title" } }
        """;

    private const string Broken = """
        { "format": "shirabe.metadata", "version": "0.1",
          "subject": { "type": "recording", "id": "0192f0a8-7c1e-7a53-9d0e-3b6f2a1c4d55" },
          "layer": "personal", "source": { "kind": "user", "created": "2026-11-22T18:04:00Z" }, "revision": 1,
          "fields": { "title": "My title", "mood": "calm", "artwork": [ { "url": "http://example.com/a.jpg", "role": "front" } ] } }
        """;

    [Fact]
    public void It_starts_empty_with_the_invitation_to_open_a_document()
    {
        var studio = new StudioViewModel(_picker);

        Assert.True(studio.ShowEmptyState);
        Assert.False(studio.HasDocuments);
        Assert.False(studio.HasError);
    }

    [Fact]
    public async Task A_document_that_follows_the_standard_says_so()
    {
        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(Write("good.shirabe.json", Valid));

        var document = Assert.Single(studio.Documents);
        Assert.True(document.IsValid);
        Assert.Equal("Follows the standard", document.Status);
        Assert.Equal("My title", document.Heading);
        Assert.Contains("Layer: personal", document.Facts);
        Assert.Contains("From: user", document.Facts);
        Assert.Contains("Revision: 1", document.Facts);
        Assert.False(document.HasProblems);
        Assert.Contains("\"title\": \"My title\"", document.Json);
        Assert.False(studio.ShowEmptyState);
    }

    [Fact]
    public async Task A_document_that_breaks_the_standard_lists_each_problem_with_its_path()
    {
        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(Write("bad.shirabe.json", Broken));

        var document = Assert.Single(studio.Documents);
        Assert.False(document.IsValid);
        Assert.Equal((2, 0), (document.ErrorCount, document.WarningCount));
        Assert.Equal("Does not follow the standard: 2 errors", document.Status);
        Assert.Equal(["fields.mood", "fields.artwork[0]"], document.Problems.Select(p => p.Path).Order(StringComparer.Ordinal).Reverse());
    }

    [Theory]
    [InlineData(1, 0, "Does not follow the standard: 1 error")]
    [InlineData(1, 2, "Does not follow the standard: 1 error, 2 warnings")]
    [InlineData(0, 1, "Follows the standard, 1 warning")]
    [InlineData(0, 3, "Follows the standard, 3 warnings")]
    public async Task The_status_counts_errors_and_warnings(int errors, int warnings, string expected)
    {
        var json = Valid;
        if (errors > 0) json = json.Replace("\"revision\": 1", "\"revision\": 0");
        if (errors > 1) throw new InvalidOperationException("test supports one error");
        if (warnings > 0)
        {
            var nulls = string.Join(", ", Enumerable.Range(1, warnings).Select(i => $"\"example.com:w{i}\": null"));
            json = json.Replace("\"title\": \"My title\"", $"\"title\": \"My title\", {nulls}");
        }

        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(Write("n.shirabe.json", json));

        var status = studio.Documents[0].Status;
        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task A_file_with_several_documents_shows_each()
    {
        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(Write("two.shirabe.json", $"[{Valid}, {Broken}]"));

        Assert.Equal([true, false], studio.Documents.Select(d => d.IsValid));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("")]
    public async Task A_file_that_is_not_a_document_is_reported_not_thrown(string content)
    {
        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(Write("junk.json", content));

        Assert.True(studio.HasError);
        Assert.False(studio.HasDocuments);
        Assert.False(studio.ShowEmptyState);
        Assert.EndsWith("junk.json", studio.FilePath);
    }

    [Fact]
    public async Task A_missing_file_is_reported()
    {
        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(Path.Combine(_folder, "nowhere.shirabe.json"));

        Assert.Equal("The file was not found.", studio.Error);
    }

    [Fact]
    public async Task A_huge_file_is_refused_without_reading_it()
    {
        var path = Path.Combine(_folder, "huge.json");
        await using (var stream = File.Create(path)) stream.SetLength(5 * 1024 * 1024);
        var studio = new StudioViewModel(_picker);

        await studio.OpenPathAsync(path);

        Assert.Contains("larger than 4 MB", studio.Error);
    }

    [Fact]
    public async Task A_failed_open_replaces_what_was_shown_and_a_good_one_clears_the_error()
    {
        var studio = new StudioViewModel(_picker);
        await studio.OpenPathAsync(Write("good.shirabe.json", Valid));

        await studio.OpenPathAsync(Write("junk.json", "{ nope"));
        Assert.False(studio.HasDocuments);
        Assert.True(studio.HasError);

        await studio.OpenPathAsync(Write("again.shirabe.json", Valid));
        Assert.True(studio.HasDocuments);
        Assert.False(studio.HasError);
    }

    [Fact]
    public async Task The_open_command_asks_for_a_file_and_does_nothing_when_cancelled()
    {
        var studio = new StudioViewModel(_picker);

        await studio.OpenCommand.ExecuteAsync(null); // the picker returns nothing: cancelled
        Assert.True(studio.ShowEmptyState);

        _picker.OpenResult = Write("good.shirabe.json", Valid);
        await studio.OpenCommand.ExecuteAsync(null);
        Assert.Single(studio.Documents);
        Assert.Equal("*.json", _picker.LastOpenPattern);
    }

    // ---- Studio in the shell ----

    [Fact]
    public async Task Studio_is_built_only_when_first_opened_and_only_once()
    {
        var built = 0;
        var main = new MainViewModel(null, null, null, studioFactory: () =>
        {
            built++;
            return new StudioViewModel(_picker);
        });
        Assert.Equal(0, built); // a listener who never opens Studio pays nothing

        main.ShowStudioCommand.Execute(null);
        main.ShowHomeCommand.Execute(null);
        main.ShowStudioCommand.Execute(null);

        Assert.Equal(1, built);
        Assert.True(main.IsStudioSelected);
        Assert.IsType<StudioViewModel>(main.CurrentPage);
    }

    [Fact]
    public void Without_a_studio_factory_the_entry_is_unavailable()
    {
        var main = new MainViewModel(null, null, null);

        Assert.False(main.HasStudio);
        Assert.False(main.ShowStudioCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_document_the_app_is_asked_to_open_goes_to_studio()
    {
        var main = new MainViewModel(null, null, null, studioFactory: () => new StudioViewModel(_picker));

        await main.OpenInStudioAsync(Write("good.shirabe.json", Valid));

        var studio = Assert.IsType<StudioViewModel>(main.CurrentPage);
        Assert.Single(studio.Documents);
        Assert.True(main.IsStudioSelected);
    }
}

internal sealed class FakeFilePicker : IFilePicker
{
    public string? OpenResult { get; set; }

    public string? SaveResult { get; set; }

    public string? LastOpenPattern { get; private set; }

    public string? LastSuggestedName { get; private set; }

    public int SaveAsked { get; private set; }

    public Task<string?> PickOpenAsync(string title, string description, string pattern)
    {
        LastOpenPattern = pattern;
        return Task.FromResult(OpenResult);
    }

    public Task<string?> PickSaveAsync(string title, string suggestedName, string description, string pattern)
    {
        SaveAsked++;
        LastSuggestedName = suggestedName;
        return Task.FromResult(SaveResult);
    }
}

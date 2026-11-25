using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

namespace Shirabe.Tests;

public class ScrobbleStatusViewModelTests
{
    private static ScrobbleReport Report(ScrobbleResult result, string title = "Song") =>
        new(result, new LastFmTrack("Artist", title));

    [Theory]
    [InlineData(ScrobbleResult.Scrobbled, "Scrobbled Song by Artist", false)]
    [InlineData(ScrobbleResult.Ignored, "Last.fm ignored Song by Artist", true)]
    [InlineData(ScrobbleResult.Failed, "Could not scrobble Song by Artist", true)]
    public async Task A_report_shows_for_its_time_and_then_closes(ScrobbleResult result, string message, bool problem)
    {
        var time = new TaskCompletionSource();
        var status = new ScrobbleStatusViewModel(() => time.Task);
        Assert.False(status.IsOpen);
        Assert.Equal("", status.Message);

        var showing = status.ShowAsync(Report(result));

        Assert.True(status.IsOpen);
        Assert.Equal(message, status.Message);
        Assert.Equal(problem, status.IsProblem);

        time.SetResult();
        await showing;

        Assert.False(status.IsOpen);
        Assert.Equal(message, status.Message); // still there while it fades
    }

    [Fact]
    public async Task An_earlier_message_running_out_does_not_close_a_newer_one()
    {
        var times = new Queue<TaskCompletionSource>([new(), new()]);
        var first = times.Peek();
        var status = new ScrobbleStatusViewModel(() => times.Dequeue().Task);
        var showingFirst = status.ShowAsync(Report(ScrobbleResult.Scrobbled, "One"));
        var second = times.Peek();
        var showingSecond = status.ShowAsync(Report(ScrobbleResult.Scrobbled, "Two"));

        first.SetResult();
        await showingFirst;
        Assert.True(status.IsOpen);

        second.SetResult();
        await showingSecond;
        Assert.False(status.IsOpen);
    }
}

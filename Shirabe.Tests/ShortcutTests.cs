using Avalonia.Input;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;
using Shirabe.Client.ViewModels;

using Xunit;

namespace Shirabe.Tests;

public class ShortcutTests
{
    private readonly AppSettings _settings = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Defaults_name_real_keys_each_used_once_and_every_action_has_a_name(bool mac)
    {
        var actions = Enum.GetValues<ShortcutAction>();
        var gestures = actions.Select(action => Shortcuts.Default(action, mac)).Where(g => g.Length > 0).ToList();

        Assert.Equal(gestures.Count, gestures.Distinct().Count());
        foreach (var gesture in gestures)
        {
            var key = gesture.Split('+')[^1];
            // The same spelling a key press produces, or the shortcut would never match.
            Assert.Equal(key, Enum.Parse<Key>(key).ToString());
        }

        Assert.All(actions, action => Assert.NotEqual(Shortcuts.LabelKey(action), Loc.Get(Shortcuts.LabelKey(action))));
    }

    [Fact]
    public void Pressed_keys_are_written_the_way_the_defaults_are()
    {
        Assert.Equal("Space", Shortcuts.Gesture(false, false, false, false, Key.Space.ToString()));
        Assert.Equal("Ctrl+Left", Shortcuts.Gesture(true, false, false, false, Key.Left.ToString()));
        Assert.Equal("Ctrl+Alt+Shift+Meta+A", Shortcuts.Gesture(true, true, true, true, "A"));
        Assert.Equal("Ctrl+,", Shortcuts.Display("Ctrl+OemComma"));
        Assert.Equal("Ctrl+1", Shortcuts.Display("Ctrl+D1"));
        Assert.Equal("Delete", Shortcuts.Display("Delete"));
    }

    [Fact]
    public void A_changed_key_is_found_and_saved_and_reset_brings_the_defaults_back()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = AppSettings.Load(path);
            Shortcuts.Set(settings, ShortcutAction.Mute, "K");
            Shortcuts.Set(settings, ShortcutAction.Repeat, "");

            var loaded = AppSettings.Load(path);

            Assert.Equal(ShortcutAction.Mute, Shortcuts.Find(loaded, "K"));
            Assert.Null(Shortcuts.Find(loaded, "M"));
            Assert.Null(Shortcuts.Find(loaded, "R"));
            Assert.Null(Shortcuts.Find(loaded, ""));

            Shortcuts.ResetAll(loaded);

            Assert.Equal(ShortcutAction.Mute, Shortcuts.Find(AppSettings.Load(path), "M"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_key_the_user_chose_wins_over_a_default_and_a_null_in_the_file_means_the_defaults()
    {
        _settings.Shortcuts = new() { [nameof(ShortcutAction.Queue)] = "S" }; // Shuffle's default
        Assert.Equal(ShortcutAction.Queue, Shortcuts.Find(_settings, "S"));

        _settings.Shortcuts = null!;
        Assert.Equal(ShortcutAction.Shuffle, Shortcuts.Find(_settings, "S"));
        Shortcuts.Set(_settings, ShortcutAction.Mute, "K");
        Assert.Equal(ShortcutAction.Mute, Shortcuts.Find(_settings, "K"));

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{ "Shortcuts": null }""");
            Assert.Equal(ShortcutAction.Mute, Shortcuts.Find(AppSettings.Load(path), "M"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Capturing_saves_the_next_key_and_refuses_one_that_is_taken()
    {
        var (page, row) = Capture(ShortcutAction.Mute);

        Assert.True(page.Press(row, "LeftCtrl", "Ctrl+LeftCtrl"));
        Assert.True(row.IsCapturing);
        Assert.True(page.Press(row, "S", "S"));

        Assert.True(row.IsCapturing);
        Assert.Contains("Shuffle", row.Message);
        Assert.Equal("M", Shortcuts.Get(_settings, ShortcutAction.Mute));

        Assert.True(page.Press(row, "K", "K"));

        Assert.False(row.IsCapturing);
        Assert.Null(row.Message);
        Assert.Equal("K", row.KeyText);
    }

    [Theory]
    [InlineData("Escape", true, "M")]
    [InlineData("Tab", false, "M")] // left alone, so the focus can move on
    [InlineData("Back", true, "")]
    [InlineData("Delete", true, "")]
    public void Some_keys_cancel_or_remove_while_capturing(string key, bool usedUp, string after)
    {
        var (page, row) = Capture(ShortcutAction.Mute);

        Assert.Equal(usedUp, page.Press(row, key, key));

        Assert.False(row.IsCapturing);
        Assert.Equal(after, Shortcuts.Get(_settings, ShortcutAction.Mute));
    }

    [Fact]
    public void Tooltips_name_the_current_key()
    {
        var hints = ShortcutHints.Instance;
        hints.Attach(_settings);

        Assert.Equal("Shuffle (S)", hints["Shuffle"]);
        Assert.Equal(" (M)", hints["Key.Mute"]);
        Assert.Equal("", hints["Key.PlayPause"]); // no key by default

        Shortcuts.Set(_settings, ShortcutAction.Shuffle, "");
        Assert.Equal("Shuffle", hints["Shuffle"]);
    }

    [Fact]
    public async Task Running_a_shortcut_acts_on_the_player_and_the_pages()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var engine = new FakeAudioPlayer();
        var shell = new MainViewModel(new PlayerViewModel(engine), null, null);
        var player = shell.Player!;

        Assert.True(shell.Run(ShortcutAction.PlayPause)); // nothing loaded: used up, nothing happens
        Assert.Equal(default, engine.State);

        await player.PlayAsync(new(Guid.NewGuid(), "A", null, null, null,
            new Uri(Path.Combine(Path.GetTempPath(), "a.mp3")).AbsoluteUri));
        engine.Position = TimeSpan.FromSeconds(10);
        shell.Run(ShortcutAction.SeekForward);
        shell.Run(ShortcutAction.VolumeDown);
        shell.Run(ShortcutAction.Shuffle);
        shell.Run(ShortcutAction.Queue);
        shell.Run(ShortcutAction.Settings);

        Assert.Equal(TimeSpan.FromSeconds(15), engine.Seeks[^1]);
        Assert.Equal(0.95, player.Volume, 3);
        Assert.True(player.IsShuffleOn);
        Assert.True(player.IsQueueOpen);
        Assert.True(shell.IsSettingsSelected);

        shell.Run(ShortcutAction.Songs); // no library: stays where it is
        Assert.True(shell.IsSettingsSelected);

        Assert.True(shell.ClosePanel());
        Assert.False(player.IsQueueOpen);
        Assert.False(shell.ClosePanel());
    }

    private (ShortcutsSettingsViewModel Page, ShortcutRowViewModel Row) Capture(ShortcutAction action)
    {
        var page = new ShortcutsSettingsViewModel(_settings);
        var row = page.Rows.Single(r => r.Action == action);
        row.StartCaptureCommand.Execute(null);
        return (page, row);
    }
}
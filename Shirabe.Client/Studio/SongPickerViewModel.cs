using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shirabe.Client.ViewModels;
using Shirabe.Core.Library;

namespace Shirabe.Client.Studio;

/// <summary>Choosing one song of the library with a search box, and, when a new document is made, the layer it is written in.</summary>
public partial class SongPickerViewModel : ViewModelBase
{
    /// <summary>The layers a person can start a document in, as they are said to people.</summary>
    public static IReadOnlyList<string> LayerNames { get; } =
        ["Personal: my own notes", "Artist: official information about the song", "Community: a contribution"];

    private static readonly string[] LayerKeys = ["personal", "canonical", "community"];

    private readonly IReadOnlyList<TrackSummary> _all;

    /// <param name="actionText">What the button that confirms the choice says: "Create", "Choose".</param>
    /// <param name="chooseLayer">Also ask which layer the new document is written in.</param>
    public SongPickerViewModel(IReadOnlyList<TrackSummary> songs, string heading, string actionText, bool chooseLayer)
    {
        _all = songs;
        Heading = heading;
        ActionText = actionText;
        ChooseLayer = chooseLayer;
        Songs = SongFilter.Filter(songs, "");
    }

    public string Heading { get; }

    public string ActionText { get; }

    public bool ChooseLayer { get; }

    [ObservableProperty]
    public partial int SelectedLayerIndex { get; set; }

    /// <summary>"personal", "canonical" or "community".</summary>
    public string SelectedLayer => LayerKeys[Math.Clamp(SelectedLayerIndex, 0, LayerKeys.Length - 1)];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<TrackSummary> Songs { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    public partial TrackSummary? SelectedSong { get; set; }

    /// <summary>The person confirmed the choice: <see cref="SelectedSong"/> (and <see cref="SelectedLayer"/>).</summary>
    public event EventHandler? Picked;

    public event EventHandler? Closed;

    partial void OnFilterChanged(string value) => Songs = SongFilter.Filter(_all, value);

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void Choose() => Picked?.Invoke(this, EventArgs.Empty);

    private bool CanChoose() => SelectedSong is not null;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}

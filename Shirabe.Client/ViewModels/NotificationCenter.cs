using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shirabe.Client.ViewModels;

/// <summary>
/// The in-app notifications that sit over the bottom right of the shell until the person dismisses them. Texts are
/// given already translated: a notification keeps the language it was shown in. Call <see cref="Show"/> on the UI thread.
/// </summary>
public sealed class NotificationCenter
{
    public ObservableCollection<NotificationViewModel> Items { get; } = [];

    /// <param name="link">An https address the optional button opens (see <c>LinkLauncher</c>); the button needs a label too.</param>
    public NotificationViewModel Show(string title, string message, string? linkLabel = null, string? link = null)
    {
        var notification = new NotificationViewModel(this, title, message, link is null ? null : linkLabel, link);
        Items.Add(notification);
        return notification;
    }

    public void Dismiss(NotificationViewModel notification) => Items.Remove(notification);
}

public sealed partial class NotificationViewModel : ObservableObject
{
    private readonly NotificationCenter _center;

    internal NotificationViewModel(NotificationCenter center, string title, string message, string? linkLabel,
        string? link)
    {
        _center = center;
        Title = title;
        Message = message;
        LinkLabel = linkLabel;
        Link = link;
    }

    public string Title { get; }

    [ObservableProperty] public partial string Message { get; set; }

    /// <summary>What the optional action button does (the link button is separate). Set it together with its label.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    public partial IAsyncRelayCommand? Action { get; set; }

    [ObservableProperty] public partial string? ActionLabel { get; set; }

    public bool HasAction => Action is not null;

    public string? LinkLabel { get; }

    public string? Link { get; }

    public bool HasLink => Link is not null;

    [RelayCommand]
    private void Dismiss() => _center.Dismiss(this);
}
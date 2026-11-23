using CommunityToolkit.Mvvm.ComponentModel;

using Shirabe.Client.Localization;

namespace Shirabe.Client.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    // The context of the thread that created the view model, i.e. the UI thread. Null in unit tests.
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>Raises PropertyChanged for these properties when the app language changes (for texts built with <see cref="Loc.Get(string)"/>).</summary>
    // shortcut: the subscription is never removed, so use it only in view models that live as long as the app
    protected void RefreshOnLanguageChange(params string[] properties) =>
        Loc.Instance.LanguageChanged += (_, _) => OnUiThread(() =>
        {
            foreach (var property in properties) OnPropertyChanged(property);
        });

    /// <summary>Runs <paramref name="action"/> on the UI thread; inline when there is no UI context (tests).</summary>
    protected void OnUiThread(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }
}
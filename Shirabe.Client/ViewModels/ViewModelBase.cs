using CommunityToolkit.Mvvm.ComponentModel;

namespace Shirabe.Client.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    // The context of the thread that created the view model, i.e. the UI thread. Null in unit tests.
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>Runs <paramref name="action"/> on the UI thread; inline when there is no UI context (tests).</summary>
    protected void OnUiThread(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }
}

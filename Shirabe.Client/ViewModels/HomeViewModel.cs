namespace Shirabe.Client.ViewModels;

/// <summary>The landing page: for now the drop target for a single file.</summary>
/// <param name="notice">Something the user should know, e.g. that the music library could not be opened.</param>
public class HomeViewModel(string? notice = null) : ViewModelBase
{
    public string? Notice { get; } = notice;
}

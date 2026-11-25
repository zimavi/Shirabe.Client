using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shirabe.Client.Localization;
using Shirabe.Client.Services;

namespace Shirabe.Client.ViewModels;

/// <summary>The Last.fm part of Settings &gt; Connections: connect, disconnect and what the user needs to know.</summary>
public sealed partial class LastFmViewModel : ViewModelBase
{
    private readonly LastFmAccount? _account;
    private readonly Action<string> _openPage;

    /// <param name="account">Null in a build made without a Last.fm API key.</param>
    /// <param name="openPage">Opens a web address in the browser.</param>
    /// <param name="settings">Null in tests: defaults that are not saved.</param>
    public LastFmViewModel(LastFmAccount? account = null, Action<string>? openPage = null,
        AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        _account = account;
        _openPage = openPage ?? (_ => { });
        if (account is not null) account.PropertyChanged += (_, _) => Refresh();
        RefreshOnLanguageChange(nameof(Status));
    }

    /// <summary>Holds the scrobbling switch.</summary>
    public AppSettings Settings { get; }

    public bool IsSignedIn => _account?.Session is not null;

    /// <summary>The browser is open and we wait for the user to allow access there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(ShowConnect))]
    public partial bool IsWaiting { get; private set; }

    public bool ShowConnect => _account is not null && !IsSignedIn && !IsWaiting;

    public bool ShowStorageWarning => _account?.CannotSaveSession == true;

    public string Status =>
        _account is null ? Loc.Get("LastFm_Unavailable")
        : _account.Session is { } session ? Loc.Get("LastFm_SignedInAs", session.User)
        : IsWaiting ? Loc.Get("LastFm_Waiting")
        : Loc.Get("LastFm_SignedOut");

    public string? ProfileUrl => _account?.Session is { } session
        ? "https://www.last.fm/user/" + Uri.EscapeDataString(session.User)
        : null;

    public string WebsiteUrl => "https://www.last.fm/";

    /// <summary>What went wrong the last time, or null.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_account is null) return;

        Error = null;
        IsWaiting = true;
        try
        {
            await _account.SignInAsync(_openPage, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (LastFmException ex)
        {
            Error = ex.Code == LastFmException.TokenNotAuthorized
                ? Loc.Get("LastFm_NotAllowed")
                : Loc.Get("LastFm_Failed", ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Error = Loc.Get("LastFm_Unreachable");
        }
        finally
        {
            IsWaiting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_account is null) return;

        Error = null;
        if (!await _account.SignOutAsync()) Error = Loc.Get("LastFm_NotRemoved");
    }

    private void Refresh()
    {
        if (IsSignedIn) IsWaiting = false; // the sign-in may still be waiting for the keyring to save

        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ShowConnect));
        OnPropertyChanged(nameof(ShowStorageWarning));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ProfileUrl));
    }
}
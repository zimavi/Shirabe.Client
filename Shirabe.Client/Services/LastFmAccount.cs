using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Shirabe.Client.Services;

/// <summary>
/// The user's Last.fm sign-in. The session is kept in the platform's secure storage; where that does not work it
/// lives only in memory, and <see cref="CannotSaveSession"/> says so, for the user to be told. Use from one thread
/// (the UI's): the properties change there, while the storage is called in the background.
/// </summary>
public sealed partial class LastFmAccount(LastFmClient client, ISecretStore? store, TimeSpan? pollInterval = null)
    : ObservableObject
{
    private const string SecretName = "lastfm";

    /// <summary>How often <see cref="SignInAsync"/> asks before giving up: 10 minutes at the normal interval.</summary>
    public const int MaxSignInAttempts = 120;

    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);

    // One storage call at a time, in order: signing out must not overtake a save that is still waiting.
    private readonly SemaphoreSlim _storing = new(1, 1);

    private bool _saved; // the storage holds a session, as far as we know

    /// <summary>The signed-in account, or null.</summary>
    [ObservableProperty]
    public partial LastFmSession? Session { get; private set; }

    /// <summary>
    /// There is no secure storage, or it failed: a sign-in lasts until the app closes. A keyring may only fail when
    /// something is saved, so this can turn true right after signing in.
    /// </summary>
    [ObservableProperty]
    public partial bool CannotSaveSession { get; private set; } = store is null;

    /// <summary>Brings back the saved session, once, at startup.</summary>
    public async Task LoadAsync()
    {
        if (store is null) return;

        string? secret;
        try
        {
            secret = await InStoreAsync(() => store.Read(SecretName));
        }
        catch (SecretStoreException)
        {
            CannotSaveSession = true;
            return;
        }

        if (Parse(secret) is not { } saved || Session is not null) return; // signed in while we were reading
        _saved = true;
        Session = saved;
    }

    /// <summary>
    /// Opens Last.fm's permission page through <paramref name="openPage"/> and waits for the user to allow access
    /// there. Throws what <see cref="LastFmClient"/> throws, the "not authorized" error when the user never does.
    /// </summary>
    public async Task SignInAsync(Action<string> openPage, CancellationToken cancellationToken = default)
    {
        var token = await client.GetTokenAsync(cancellationToken);
        openPage(client.AuthorizationPage(token));
        var session = await WaitForApprovalAsync(token, cancellationToken);

        Session = session; // before saving: a keyring may keep us waiting for its password
        if (store is null) return;

        try
        {
            await InStoreAsync(() =>
            {
                store.Write(SecretName, JsonSerializer.Serialize(session));
                return true;
            });
            _saved = true;
            CannotSaveSession = false;
        }
        catch (SecretStoreException)
        {
            CannotSaveSession = true;
        }
    }

    /// <summary>
    /// Forgets the session. False when it could not be removed from the storage, so it may be back after a restart
    /// (the user can also revoke it on Last.fm's website).
    /// </summary>
    public async Task<bool> SignOutAsync()
    {
        Session = null;
        if (store is null) return true;

        try
        {
            await InStoreAsync(() =>
            {
                store.Delete(SecretName);
                return true;
            });
            _saved = false;
            return true;
        }
        catch (SecretStoreException)
        {
            return !_saved;
        }
    }

    private async Task<LastFmSession> WaitForApprovalAsync(string token, CancellationToken cancellationToken)
    {
        for (var attempt = 1;; attempt++)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            try
            {
                return await client.GetSessionAsync(token, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxSignInAttempts && !cancellationToken.IsCancellationRequested
                                                                   && IsWorthAskingAgain(ex))
            {
            }
        }
    }

    // Not allowed yet; or the service is offline (11), has a passing error (16), limits us (29) or cannot be reached.
    private static bool IsWorthAskingAgain(Exception ex) =>
        ex is LastFmException { Code: LastFmException.TokenNotAuthorized or 11 or 16 or 29 }
            or HttpRequestException or TaskCanceledException;

    private async Task<T> InStoreAsync<T>(Func<T> call)
    {
        await _storing.WaitAsync();
        try
        {
            return await Task.Run(call);
        }
        finally
        {
            _storing.Release();
        }
    }

    private static LastFmSession? Parse(string? secret)
    {
        if (secret is null) return null;

        try
        {
            return JsonSerializer.Deserialize<LastFmSession>(secret) is
                { User: { Length: > 0 }, Key: { Length: > 0 } } session
                ? session
                : null;
        }
        catch (JsonException)
        {
            return null; // damaged: signing in again replaces it
        }
    }
}
namespace Shirabe.Client.Services;

/// <summary>
/// Secrets such as session keys, kept in the platform's own secure storage and never in a file of ours. A call may
/// block (the system can ask the user to unlock the storage), so make it off the UI thread. Every failure of the
/// storage is a <see cref="SecretStoreException"/>.
/// </summary>
public interface ISecretStore
{
    /// <summary>The secret saved as <paramref name="name"/>, or null when there is none.</summary>
    string? Read(string name);

    /// <summary>Saves <paramref name="secret"/> as <paramref name="name"/>, replacing what was there.</summary>
    void Write(string name, string secret);

    /// <summary>Removes the secret; nothing happens when there is none.</summary>
    void Delete(string name);

    /// <summary>The secure storage of this platform, or null where Shirabe has no support for one yet.</summary>
    static ISecretStore? ForThisPlatform()
    {
        if (OperatingSystem.IsWindows()) return new WindowsSecretStore();
        return OperatingSystem.IsLinux() ? new LinuxSecretStore() : null;
    }
}

public sealed class SecretStoreException(string message, Exception? inner = null) : Exception(message, inner);
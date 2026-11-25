using Shirabe.Client.Services;

using Xunit;

namespace Shirabe.Tests;

/// <summary>Against the real Windows Credential Manager, under a random name that is removed again.</summary>
public sealed class WindowsSecretStoreTests : IDisposable
{
    private readonly string _name = $"test-{Guid.NewGuid():N}";
    private readonly ISecretStore? _store = ISecretStore.ForThisPlatform();

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) _store!.Delete(_name);
    }

    private ISecretStore Store()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the Windows Credential Manager.");
        return _store!;
    }

    [Fact]
    public void A_secret_is_saved_replaced_and_removed()
    {
        var store = Store();
        Assert.Null(store.Read(_name));

        store.Write(_name, """{"key":"пароль 🔑"}""");
        Assert.Equal("""{"key":"пароль 🔑"}""", store.Read(_name));
        Assert.Equal("""{"key":"пароль 🔑"}""", ISecretStore.ForThisPlatform()!.Read(_name)); // kept by the system

        store.Write(_name, "second");
        Assert.Equal("second", store.Read(_name));

        store.Delete(_name);
        Assert.Null(store.Read(_name));
        store.Delete(_name); // nothing to remove is fine
    }

    [Fact]
    public void An_empty_secret_is_not_the_same_as_none()
    {
        var store = Store();

        store.Write(_name, "");

        Assert.Equal("", store.Read(_name));
    }

    [Fact]
    public void The_longest_secret_fits_and_a_longer_one_is_refused_without_touching_the_saved_one()
    {
        var store = Store();
        var longest = new string('a', 1280);
        store.Write(_name, longest);
        Assert.Equal(longest, store.Read(_name));

        Assert.Throws<SecretStoreException>(() => store.Write(_name, longest + "a"));

        Assert.Equal(longest, store.Read(_name));
    }
}
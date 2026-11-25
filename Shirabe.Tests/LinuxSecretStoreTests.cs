using Shirabe.Client.Services;

using Xunit;

namespace Shirabe.Tests;

/// <summary>
/// Against the real Secret Service, under a random name that is removed again. Skipped where there is none.
/// </summary>
public sealed class LinuxSecretStoreTests
{
    [Fact]
    public void A_secret_is_saved_replaced_and_removed()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Needs Linux.");
            return;
        }

        var store = new LinuxSecretStore();
        var name = $"test-{Guid.NewGuid():N}";
        try
        {
            Assert.Null(store.Read(name));
        }
        catch (SecretStoreException ex)
        {
            Assert.Skip(ex.Message);
        }

        try
        {
            store.Write(name, """{"key":"пароль 🔑"}""");
            Assert.Equal("""{"key":"пароль 🔑"}""", new LinuxSecretStore().Read(name));

            store.Write(name, "second");
            Assert.Equal("second", store.Read(name));

            store.Delete(name);
            Assert.Null(store.Read(name));
            store.Delete(name); // nothing to remove is fine
        }
        finally
        {
            store.Delete(name);
        }
    }
}
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shirabe.Client.Services;

/// <summary>
/// Secrets in the desktop's Secret Service (GNOME Keyring, KWallet and others) through libsecret, in the default
/// collection under the schema <c>ua.net.zimavi.Shirabe</c>. Many systems have no such service, or no libsecret:
/// then every call throws <see cref="SecretStoreException"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretStore : ISecretStore
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";

    public string? Read(string name) => Call(name, (schema, attributes) =>
    {
        var password = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, out var error);
        ThrowIfFailed("read", error);
        if (password == IntPtr.Zero) return null;

        try
        {
            return Marshal.PtrToStringUTF8(password);
        }
        finally
        {
            secret_password_free(password);
        }
    });

    public void Write(string name, string secret) => Call(name, (schema, attributes) =>
    {
        // A null collection is the default one; an item with the same attributes is replaced.
        secret_password_storev_sync(schema, attributes, null, "Shirabe: " + name, secret, IntPtr.Zero, out var error);
        ThrowIfFailed("save", error);
        return true;
    });

    public void Delete(string name) => Call(name, (schema, attributes) =>
    {
        // False without an error means there was nothing to remove.
        secret_password_clearv_sync(schema, attributes, IntPtr.Zero, out var error);
        ThrowIfFailed("delete", error);
        return true;
    });

    /// <summary>Runs a libsecret call with the schema and the attributes that identify <paramref name="name"/>.</summary>
    private static T Call<T>(string name, Func<IntPtr, IntPtr, T> call)
    {
        var key = Marshal.StringToCoTaskMemUTF8("name");
        var value = Marshal.StringToCoTaskMemUTF8(name);
        IntPtr types = IntPtr.Zero, schema = IntPtr.Zero, attributes = IntPtr.Zero;
        try
        {
            var glib = NativeLibrary.Load(GLib);
            var hash = NativeLibrary.GetExport(glib, "g_str_hash");
            var equal = NativeLibrary.GetExport(glib, "g_str_equal");

            types = g_hash_table_new(hash, equal);
            g_hash_table_insert(types, key, IntPtr.Zero); // SECRET_SCHEMA_ATTRIBUTE_STRING
            schema = secret_schema_newv("ua.net.zimavi.Shirabe", 0, types);

            attributes = g_hash_table_new(hash, equal); // it borrows the strings, freed below
            g_hash_table_insert(attributes, key, value);

            return call(schema, attributes);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException($"libsecret cannot be used: {ex.Message}", ex);
        }
        finally
        {
            if (attributes != IntPtr.Zero) g_hash_table_unref(attributes);
            if (schema != IntPtr.Zero) secret_schema_unref(schema);
            if (types != IntPtr.Zero) g_hash_table_unref(types);
            Marshal.FreeCoTaskMem(key);
            Marshal.FreeCoTaskMem(value);
        }
    }

    private static void ThrowIfFailed(string action, IntPtr error)
    {
        if (error == IntPtr.Zero) return;

        // GError: a 32-bit domain, a 32-bit code, then the message
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
        g_error_free(error);
        throw new SecretStoreException($"The secret service could not {action} the secret: {message}");
    }

    [DllImport(GLib)]
    private static extern IntPtr g_hash_table_new(IntPtr hashFunction, IntPtr equalFunction);

    [DllImport(GLib)]
    private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GLib)]
    private static extern void g_hash_table_unref(IntPtr table);

    [DllImport(GLib)]
    private static extern void g_error_free(IntPtr error);

    [DllImport(LibSecret)]
    private static extern IntPtr secret_schema_newv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags,
        IntPtr attributeTypes);

    [DllImport(LibSecret)]
    private static extern void secret_schema_unref(IntPtr schema);

    [DllImport(LibSecret)]
    private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibSecret)]
    private static extern void secret_password_free(IntPtr password);
}
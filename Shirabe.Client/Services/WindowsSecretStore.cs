using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace Shirabe.Client.Services;

/// <summary>
/// Secrets as "generic credentials" of the Windows Credential Manager, named <c>Shirabe/&lt;name&gt;</c>. Windows
/// encrypts them for the signed-in user and keeps them on this machine (they do not roam). At most 2560 bytes, which
/// is 1280 UTF-16 characters.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecretStore : ISecretStore
{
    private const uint Generic = 1; // CRED_TYPE_GENERIC
    private const uint LocalMachine = 2; // CRED_PERSIST_LOCAL_MACHINE
    private const int NotFound = 1168; // ERROR_NOT_FOUND

    public string? Read(string name)
    {
        if (!CredRead(Target(name), Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == NotFound ? null : throw Failure("read", error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var blob = new byte[credential.CredentialBlobSize];
            if (blob.Length > 0) Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.Unicode.GetString(blob);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string name, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var pinned = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var credential = new Credential
            {
                Type = Generic,
                TargetName = Target(name),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = pinned.AddrOfPinnedObject(),
                Persist = LocalMachine,
            };
            if (!CredWrite(in credential, 0)) throw Failure("save", Marshal.GetLastPInvokeError());
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Delete(string name)
    {
        if (CredDelete(Target(name), Generic, 0)) return;

        var error = Marshal.GetLastPInvokeError();
        if (error != NotFound) throw Failure("delete", error);
    }

    private static string Target(string name) => "Shirabe/" + name;

    private static SecretStoreException Failure(string action, int error)
    {
        var cause = new Win32Exception(error);
        return new SecretStoreException(
            $"Windows Credential Manager could not {action} the secret: {cause.Message} (error {error})", cause);
    }

    // CREDENTIALW from wincred.h
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredWrite(in Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr credential);
}
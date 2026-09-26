using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using JUtility.Core.Credentials;
using JUtility.Core.Reports;

namespace JUtility.App.Services;

// Generic credentials in the Windows Credential Manager (visible under "Windows Credentials"), protected by
// Windows for the current user. Power Ops never writes these values to its own files.
internal sealed class WindowsCredentialVault : ICredentialVault, ISecretVault
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public BasicCredential? Read(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, "Could not read the saved Mongoku sign-in from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            string password = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return new BasicCredential(credential.UserName ?? string.Empty, password);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string target, BasicCredential value)
    {
        ReportAuth.Validate(value);
        WriteBlob(target, value.UserName, value.Password, "Power Ops: sign-in for Mongoku report cards and the embedded Mongoku tab",
            "Could not save the Mongoku sign-in in Windows Credential Manager.");
    }

    // ---- ISecretVault: Credentials & IDs secrets under opaque PowerOps/Secret/{scope}/{record} targets ----

    public bool Exists(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return false;
            throw new Win32Exception(error, "Could not check Windows Credential Manager.");
        }
        CredFree(pointer);
        return true;
    }

    public string? ReadSecret(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, "Could not read the secret from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void WriteSecret(string target, string secret, string userName, string comment)
    {
        if (!target.StartsWith(CredentialRules.VaultPrefix, StringComparison.Ordinal)) throw new InvalidDataException("Not a Power Ops secret target.");
        SecretValues.Validate(secret);
        WriteBlob(target, string.IsNullOrWhiteSpace(userName) ? "PowerOps" : userName.Trim(), secret,
            comment.Length > 200 ? comment[..200] : comment, "Could not save the secret in Windows Credential Manager.");
    }

    public IReadOnlyList<string> Targets(string prefix)
    {
        if (!CredEnumerate(prefix + "*", 0, out int count, out IntPtr list))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return [];
            throw new Win32Exception(error, "Could not list Power Ops entries in Windows Credential Manager.");
        }

        try
        {
            var targets = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr item = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                var credential = Marshal.PtrToStructure<Credential>(item);
                if (credential.Type == CredTypeGeneric && credential.TargetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) targets.Add(credential.TargetName);
            }
            return targets;
        }
        finally
        {
            CredFree(list);
        }
    }

    private static void WriteBlob(string target, string userName, string secret, string comment, string failure)
    {
        byte[] blob = Encoding.Unicode.GetBytes(secret);
        IntPtr blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = comment,
                CredentialBlob = blobPointer,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), failure);
            }
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length); // wipe the unmanaged copy of the password
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPointer);
        }
    }

    public bool Delete(string target)
    {
        if (CredDelete(target, CredTypeGeneric, 0)) return true;
        int error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound) return false;
        throw new Win32Exception(error, "Could not remove the entry from Windows Credential Manager.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

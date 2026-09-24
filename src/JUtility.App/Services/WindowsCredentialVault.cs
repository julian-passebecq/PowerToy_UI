using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using JUtility.Core.Reports;

namespace JUtility.App.Services;

// Generic credentials in the Windows Credential Manager (visible under "Windows Credentials"), protected by
// Windows for the current user. Power Ops never writes these values to its own files.
internal sealed class WindowsCredentialVault : ICredentialVault
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
        byte[] blob = Encoding.Unicode.GetBytes(value.Password);
        IntPtr blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = "Power Ops: sign-in for Mongoku report cards and the embedded Mongoku tab",
                CredentialBlob = blobPointer,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
                UserName = value.UserName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the Mongoku sign-in in Windows Credential Manager.");
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
        throw new Win32Exception(error, "Could not remove the Mongoku sign-in from Windows Credential Manager.");
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

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

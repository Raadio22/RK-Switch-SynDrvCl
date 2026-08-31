using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace RKSwitch.SynDrvCl;

internal static class WindowsCredentialStore
{
    private const string TargetName = "RK-Switch-SynDrvCl/Synology-DSM";
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static bool Exists
    {
        get
        {
            if (!TryRead(out var password)) return false;
            password?.Dispose();
            return true;
        }
    }

    public static bool TryRead([NotNullWhen(true)] out SecureString? password)
    {
        password = null;
        if (!CredRead(TargetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return false;
            throw new Win32Exception(error, "Heslo nelze načíst ze Správce přihlašovacích údajů Windows.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return false;
            if (credential.CredentialBlobSize % 2 != 0)
                throw new InvalidOperationException("Uložené heslo má neplatný formát.");

            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                var secure = new SecureString();
                for (var i = 0; i < bytes.Length; i += 2)
                    secure.AppendChar((char)(bytes[i] | bytes[i + 1] << 8));
                secure.MakeReadOnly();
                password = secure;
                return secure.Length > 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static void Save(SecureString password)
    {
        if (password.Length == 0) throw new ArgumentException("Heslo nesmí být prázdné.", nameof(password));

        var blob = Marshal.SecureStringToCoTaskMemUnicode(password);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = TargetName,
                Comment = "Heslo DSM pro RK-Switch SynDrvCl",
                CredentialBlobSize = checked((uint)(password.Length * sizeof(char))),
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "Synology DSM"
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Heslo nelze uložit do Správce přihlašovacích údajů Windows.");
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public static void Delete()
    {
        if (CredDelete(TargetName, CredentialTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error, "Heslo nelze odstranit ze Správce přihlašovacích údajů Windows.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

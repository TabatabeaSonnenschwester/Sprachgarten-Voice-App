using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Klartext;

// Only unconfirmed clipboard results survive a restart, encrypted for the Windows user.
public static class RecoveryStore
{
    private static string PathName => Path.Combine(AppPaths.Root, "uncopied.bin");
    public static bool Exists => File.Exists(PathName);
    public static void Save(string text)
    {
        // Sprint F (Antwort 2): dasselbe Ersetzen wie ProtectedFile.Write; die Temp-Datei wird abgesichert weggeräumt.
        string temp = PathName + ".tmp";
        try
        {
            File.WriteAllBytes(temp, WindowsUserProtection.Transform(Encoding.UTF8.GetBytes(text), true));
            ProtectedFile.MoveReplacing(temp, PathName);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
    public static string? Load() => Exists ? Encoding.UTF8.GetString(WindowsUserProtection.Transform(File.ReadAllBytes(PathName), false)) : null;
    public static void Clear() { if (Exists) File.Delete(PathName); }
}

internal static class WindowsUserProtection
{
    internal static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}

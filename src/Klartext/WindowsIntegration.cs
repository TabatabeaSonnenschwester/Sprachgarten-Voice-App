using System.Runtime.InteropServices;
using System.Text;

namespace Klartext;

public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private int activeId;
    private int nextId = 100;
    public HotkeySpec? Registered { get; private set; }
    public event Action? Pressed;
    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "Klartext Hotkey", Parent = new IntPtr(-3) });
    internal static readonly HotkeySpec[] Suggestions = [new(6, 120), new(6, 121), new(6, 119), new(7, 120), new(7, 121)];
    internal static HotkeySpec? FindAvailable()
    {
        // Probe using a separate handle; the current shortcut stays registered.
        using var probe = new HotkeyWindow();
        return Suggestions.FirstOrDefault(probe.TryRegister);
    }
    public bool TryRegister(HotkeySpec spec)
    {
        if (!spec.IsValid) return false;
        if (spec == Registered) return true;
        int candidate = nextId++;
        if (!Native.RegisterHotKey(Handle, candidate, spec.Modifiers | 0x4000, spec.Key)) return false;
        if (activeId != 0) Native.UnregisterHotKey(Handle, activeId);
        activeId = candidate; Registered = spec; return true;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam.ToInt32() == activeId) Pressed?.Invoke();
        base.WndProc(ref m);
    }
    public void Dispose() { if (activeId != 0) Native.UnregisterHotKey(Handle, activeId); DestroyHandle(); }
}

public static class UnicodeClipboard
{
    public static async Task<bool> CopyAsync(IntPtr owner, string text, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return false;
        for (int i = 0; i < 8; i++)
        {
            token.ThrowIfCancellationRequested();
            if (TryCopy(owner, text)) return true;
            if (i < 7) await Task.Delay(60 + i * 40, token);
        }
        return false;
    }
    public static bool TryCopy(IntPtr owner, string text)
    {
        if (owner == IntPtr.Zero || string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return false;
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = Native.GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) return false;
        bool opened = false;
        try
        {
            IntPtr pointer = Native.GlobalLock(memory);
            if (pointer == IntPtr.Zero) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { Native.GlobalUnlock(memory); }
            if (!Native.OpenClipboard(owner)) return false;
            opened = true;
            if (!Native.EmptyClipboard() || Native.SetClipboardData(13, memory) == IntPtr.Zero) return false;
            memory = IntPtr.Zero; // Ownership transferred to Windows.
            return true;
        }
        finally
        {
            if (opened) Native.CloseClipboard();
            if (memory != IntPtr.Zero) Native.GlobalFree(memory);
        }
    }
}

public static class Native
{
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr memory);
}

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpanDraft.StoreScreenshots;

/// <summary>Allows the measured capture size on this HWND, even beyond the desktop bounds.</summary>
internal sealed class WindowSizeOverride : IDisposable
{
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data);
    private readonly SubclassProc callback;
    private readonly IntPtr hwnd;
    private readonly int width, height;

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam);

    internal WindowSizeOverride(IntPtr hwnd, int width, int height)
    {
        NativeWindows.AssertOwnWindow(hwnd);
        this.hwnd = hwnd; this.width = width; this.height = height;
        callback = ProcessMessage;
        if (!SetWindowSubclass(hwnd, callback, UIntPtr.Zero, UIntPtr.Zero)) throw new Win32Exception();
    }

    private IntPtr ProcessMessage(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data)
    {
        var result = DefSubclassProc(window, message, wparam, lparam);
        if (message != 0x24) return result; // WM_GETMINMAXINFO
        ApplyTrackSize(lparam, width, height);
        return IntPtr.Zero;
    }

    internal static void ApplyTrackSize(IntPtr minMaxInfo, int width, int height)
    {
        // MINMAXINFO.ptMaxTrackSize follows four POINTs; retain all other native constraints.
        Marshal.WriteInt32(minMaxInfo, 32, Math.Max(Marshal.ReadInt32(minMaxInfo, 32), width));
        Marshal.WriteInt32(minMaxInfo, 36, Math.Max(Marshal.ReadInt32(minMaxInfo, 36), height));
    }

    public void Dispose()
    {
        // Destruction of the HWND also removes its subclass; disposal is safe in either case.
        RemoveWindowSubclass(hwnd, callback, UIntPtr.Zero);
        GC.KeepAlive(callback);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpanDraft.StoreScreenshots;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(NativeRect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
}

public sealed record DisplayDevice(string Name, string Description, string Identity, uint State,
    int Width, int Height, int X, int Y, int ScalePercent)
{
    public bool Primary => (State & 4) != 0;
    public bool Active => (State & 1) != 0;
}

public sealed record DisplaySnapshot(DisplayDevice[] Devices, string Target)
{
    public DisplayDevice CaptureDisplay => Devices.Single(d => d.Name == Target);

    public static DisplaySnapshot RequireTarget(DisplayDevice[] devices, string? targetName = null)
    {
        if (devices.Count(d => d.Primary) != 1) throw new InvalidOperationException("Cannot identify exactly one primary display.");
        var target = devices.Where(d => d.Active && d.Width > 0 && d.Height > 0 && d.ScalePercent == 200)
            .OrderByDescending(d => d.Primary).ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault(d => targetName is null || d.Name == targetName)
            ?? throw new InvalidOperationException("An active display at 200% (192 DPI) is required. "
                + "Found: " + string.Join("; ", devices.Select(d => $"{d.Name}: {d.Width} x {d.Height} px, {d.ScalePercent}%, active={d.Active}"))
                + ". No display was changed.");
        return new(devices, target.Name);
    }
}

public sealed record CaptionEvidence(bool NativeCaption, bool SystemMenu, bool Minimize, bool Maximize,
    bool Icon, NativeRect CaptionButtons);

/// <summary>Display configuration is read-only; only the harness's own HWND is positioned.</summary>
internal static class NativeWindows
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? name, uint index, ref Device device, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string name, int index, IntPtr data);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr path, ref uint modes, IntPtr mode, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(IntPtr data);
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern IntPtr GetClassLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    private static IntPtr Allocate(int size)
    {
        var pointer = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, pointer, size);
        return pointer;
    }

    internal static DisplayDevice[] Displays()
    {
        var scales = DisplayScales();
        var result = new List<DisplayDevice>();
        for (uint i = 0; ; i++)
        {
            var device = new Device { Size = Marshal.SizeOf<Device>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            var data = Allocate(220);
            try
            {
                Marshal.WriteInt16(data, 68, 220);
                bool hasMode = EnumDisplaySettings(device.Name, -1, data);
                result.Add(new(device.Name, device.Description, device.Id, device.State,
                    hasMode ? Marshal.ReadInt32(data, 172) : 0, hasMode ? Marshal.ReadInt32(data, 176) : 0,
                    hasMode ? Marshal.ReadInt32(data, 76) : 0, hasMode ? Marshal.ReadInt32(data, 80) : 0,
                    scales.GetValueOrDefault(device.Name)));
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        return result.ToArray();
    }

    private static Dictionary<string, int> DisplayScales()
    {
        // Read per-source DPI offsets without changing the display configuration.
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int[] percentages = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(2, out uint pathsCount, out uint modesCount));
            var paths = Allocate(checked((int)pathsCount * 72));
            var modes = Allocate(checked((int)modesCount * 64));
            try
            {
                int status = QueryDisplayConfig(2, ref pathsCount, paths, ref modesCount, modes, IntPtr.Zero);
                if (status == 122) continue; // Topology changed between the size query and the read.
                Check(status);
                for (int i = 0; i < pathsCount; i++)
                {
                    var path = IntPtr.Add(paths, i * 72);
                    var name = Allocate(84); var dpi = Allocate(32);
                    try
                    {
                        void Header(IntPtr pointer, int type, int size)
                        {
                            Marshal.WriteInt32(pointer, 0, type); Marshal.WriteInt32(pointer, 4, size);
                            Marshal.WriteInt64(pointer, 8, Marshal.ReadInt64(path));
                            Marshal.WriteInt32(pointer, 16, Marshal.ReadInt32(path, 8));
                        }
                        Header(name, 1, 84); Check(DisplayConfigGetDeviceInfo(name));
                        Header(dpi, -3, 32); Check(DisplayConfigGetDeviceInfo(dpi));
                        string deviceName = Marshal.PtrToStringUni(IntPtr.Add(name, 20))!;
                        int index = Marshal.ReadInt32(dpi, 24) - Marshal.ReadInt32(dpi, 20);
                        if (index < 0 || index >= percentages.Length) throw new InvalidOperationException("Unknown display DPI offset.");
                        result[deviceName] = percentages[index];
                    }
                    finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(dpi); }
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
        }
        throw new InvalidOperationException("Display topology did not stabilize.");
    }

    internal static void AssertOwnWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId != Environment.ProcessId) throw new InvalidOperationException("Refusing to position a window from another process.");
    }

    internal static void Position(IntPtr hwnd, int x, int y, int width, int height)
    {
        AssertOwnWindow(hwnd);
        if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, 0x0040)) throw new Win32Exception();
    }

    internal static NativeRect WindowRect(IntPtr hwnd) => GetWindowRect(hwnd, out var rect) ? rect : throw new Win32Exception();
    internal static NativeRect ClientRect(IntPtr hwnd) => GetClientRect(hwnd, out var rect) ? rect : throw new Win32Exception();
    internal static NativeRect DwmRect(IntPtr hwnd, int attribute = 9)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(hwnd, attribute, out var rect, 16)); return rect;
    }
    internal static void Flush() => Marshal.ThrowExceptionForHR(DwmFlush());

    internal static CaptionEvidence Caption(IntPtr hwnd)
    {
        long style = (uint)GetWindowLongW(hwnd, -16);
        bool icon = SendMessageW(hwnd, 0x007F, new(2), IntPtr.Zero) != IntPtr.Zero
            || SendMessageW(hwnd, 0x007F, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero
            || SendMessageW(hwnd, 0x007F, new(1), IntPtr.Zero) != IntPtr.Zero
            || GetClassLongPtrW(hwnd, -34) != IntPtr.Zero || GetClassLongPtrW(hwnd, -14) != IntPtr.Zero;
        var buttons = DwmRect(hwnd, 5); // DWMWA_CAPTION_BUTTON_BOUNDS, relative to GetWindowRect origin.
        var window = WindowRect(hwnd); var visible = DwmRect(hwnd);
        var screenButtons = new NativeRect(window.Left + buttons.Left, window.Top + buttons.Top,
            window.Left + buttons.Right, window.Top + buttons.Bottom);
        var evidence = new CaptionEvidence((style & 0x00C00000) == 0x00C00000, (style & 0x00080000) != 0,
            (style & 0x00020000) != 0, (style & 0x00010000) != 0, icon, screenButtons);
        if (!evidence.NativeCaption || !evidence.SystemMenu || !evidence.Minimize || !evidence.Maximize || !icon
            || buttons.Width <= 0 || buttons.Height <= 0 || !visible.Contains(screenButtons))
            throw new InvalidOperationException("Native caption, icon or caption buttons are missing or outside the capture bounds.");
        return evidence;
    }

    private static void Check(int code) { if (code != 0) throw new Win32Exception(code); }
}

public static class WindowSizing
{
    public static (int Width, int Height) OuterSize(CaptureSize target, NativeRect outer, NativeRect visible)
    {
        if (!outer.Contains(visible) || visible.Width <= 0 || visible.Height <= 0)
            throw new ArgumentException("Invalid native window measurements.");
        var pixels = target.Pixels;
        return (checked(pixels.Width + outer.Width - visible.Width), checked(pixels.Height + outer.Height - visible.Height));
    }
}

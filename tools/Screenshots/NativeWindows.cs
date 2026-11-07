using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpanDraft.Screenshots;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(NativeRect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
}

public sealed record DisplayDevice(string Name, string Description, string Identity, uint State,
    int Width, int Height, int X, int Y, uint Dpi)
{
    public bool Primary => (State & 4) != 0;
    public bool Active => (State & 1) != 0;
    public double ScalePercent => Dpi * 100d / 96;
}

public sealed record DisplaySnapshot(DisplayDevice[] Devices, string Target)
{
    public DisplayDevice CaptureDisplay => Devices.Single(d => d.Name == Target);

    public static DisplaySnapshot RequireTarget(DisplayDevice[] devices, int scalePercent, string? targetName = null)
    {
        if (devices.Count(d => d.Primary) != 1) throw new InvalidOperationException("Cannot identify exactly one primary display.");
        uint dpi = CaptureScaling.ExpectedDpi(scalePercent);
        var target = devices.Where(d => d.Active && d.Width > 0 && d.Height > 0 && d.Dpi == dpi)
            .OrderByDescending(d => d.Primary).ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault(d => targetName is null || d.Name == targetName)
            ?? throw new InvalidOperationException($"An active display at {scalePercent}% ({dpi} DPI) is required. "
                + "Found: " + string.Join("; ", devices.Select(d => $"{d.Name}: {d.Width} x {d.Height} px, {d.Dpi} DPI, active={d.Active}"))
                + ". No display was changed.");
        return new(devices, target.Name);
    }

    public static void AssertScaleOnlyTransition(DisplayDevice[] before, DisplayDevice[] after)
    {
        if (!before.Select(d => d with { Dpi = 0 }).SequenceEqual(after.Select(d => d with { Dpi = 0 })))
            throw new InvalidOperationException("Only scaling may change at a phase boundary; display identity, activation, resolution and position must remain unchanged.");
    }
}

public sealed record CaptionEvidence(bool NativeCaption, bool SystemMenu, bool Minimize, bool Maximize,
    bool Icon, NativeRect CaptionButtons);

/// <summary>Measures displays and positions only the harness's own HWND. Scaling writes live in NativeDisplayScaling.</summary>
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
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    private static IntPtr Allocate(int size)
    {
        var pointer = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, pointer, size);
        return pointer;
    }

    internal static DisplayDevice[] Displays()
    {
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
                int width = hasMode ? Marshal.ReadInt32(data, 172) : 0, height = hasMode ? Marshal.ReadInt32(data, 176) : 0;
                int x = hasMode ? Marshal.ReadInt32(data, 76) : 0, y = hasMode ? Marshal.ReadInt32(data, 80) : 0;
                result.Add(new(device.Name, device.Description, device.Id, device.State,
                    width, height, x, y, (device.State & 1) != 0 && width > 0 && height > 0
                        ? MeasureDpi(device.Name, x + width / 2, y + height / 2) : 0));
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        return result.ToArray();
    }

    private static uint MeasureDpi(string deviceName, int x, int y)
    {
        // Scope awareness here as runner continuations can execute on another thread.
        IntPtr previous = SetThreadDpiAwarenessContext(new(-4));
        if (previous == IntPtr.Zero) throw new Win32Exception();
        IntPtr probe = IntPtr.Zero;
        try
        {
            // A hidden top-level system-class HWND measures custom scaling without a percentage table.
            probe = CreateWindowExW(0, "STATIC", "", 0x80000000, x, y, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (probe == IntPtr.Zero) throw new Win32Exception();
            AssertDisplay(probe, deviceName);
            uint dpi = GetDpiForWindow(probe);
            return dpi != 0 ? dpi : throw new InvalidOperationException("Native display DPI could not be measured.");
        }
        finally
        {
            try { if (probe != IntPtr.Zero && !DestroyWindow(probe)) throw new Win32Exception(); }
            finally { if (SetThreadDpiAwarenessContext(previous) == IntPtr.Zero) throw new Win32Exception(); }
        }
    }

    internal static void AssertDisplay(IntPtr hwnd, string deviceName)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(hwnd, 0), ref info)) throw new Win32Exception();
        if (!info.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native window is on a different display than the selected capture display.");
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
}

public static class WindowSizing
{
    public static void AssertMinimum(CaptureSize target, NativeRect visible, NativeRect client, double scaling, double minWidth, double minHeight)
    {
        var pixels = target.Pixels;
        if (pixels.Width - (visible.Width - client.Width) + 1 < minWidth * scaling
            || pixels.Height - (visible.Height - client.Height) + 1 < minHeight * scaling)
            throw new InvalidOperationException("The capture size at this scale is below the real window minimum. Choose a lower scale for this profile; window minimums are never overridden.");
    }

    public static (int Width, int Height) OuterSize(CaptureSize target, NativeRect outer, NativeRect visible)
    {
        if (!outer.Contains(visible) || visible.Width <= 0 || visible.Height <= 0)
            throw new ArgumentException("Invalid native window measurements.");
        var pixels = target.Pixels;
        return (checked(pixels.Width + outer.Width - visible.Width), checked(pixels.Height + outer.Height - visible.Height));
    }
}

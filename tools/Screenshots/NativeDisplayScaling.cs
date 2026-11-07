using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpanDraft.Screenshots;

internal sealed record DisplayScale(long AdapterId, uint SourceId, int Minimum, int Current, int Maximum)
{
    // Windows reports offsets from its recommended scale, not percentages.
    // These private packet types are isolated here; measured HWND DPI remains authoritative.
    private static readonly int[] Steps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    internal int Percent => Steps[checked(Current - Minimum)];

    internal int Relative(int percent)
    {
        Validate();
        int index = Array.IndexOf(Steps, percent);
        if (index < 0 || index + Minimum > Maximum)
            throw new InvalidOperationException($"Automatic display scaling does not support {percent}% on this display. Use an already scaled display for custom percentages.");
        return index + Minimum;
    }

    internal void Validate()
    {
        if (Minimum > 0 || Maximum < 0 || Current < Minimum || Current > Maximum
            || (long)Maximum - Minimum >= Steps.Length)
            throw new InvalidOperationException("Windows returned an unknown DPI scale range; refusing to change it.");
    }
}

internal static class NativeDisplayScaling
{
    // DISPLAYCONFIG_DEVICE_INFO_HEADER uses 4-byte packing, including its LUID.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Header { public int Type, Size; public long AdapterId; public uint SourceId; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ScaleGet { public Header Header; public int Minimum, Current, Maximum; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ScaleSet { public Header Header; public int Relative; }
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr path, ref uint modes, IntPtr mode, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName data);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetScale(ref ScaleGet data);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] private static extern int SetScale(ref ScaleSet data);

    internal static DisplayScale Read(string displayName)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(2, out uint pathCount, out uint modeCount));
            IntPtr paths = Marshal.AllocHGlobal(checked((int)pathCount * 72));
            IntPtr modes = IntPtr.Zero;
            try
            {
                modes = Marshal.AllocHGlobal(checked((int)modeCount * 64));
                int status = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (status == 122) continue;
                Check(status);
                for (int i = 0; i < pathCount; i++)
                {
                    IntPtr path = IntPtr.Add(paths, i * 72);
                    var header = new Header { Type = 1, Size = Marshal.SizeOf<SourceName>(),
                        AdapterId = Marshal.ReadInt64(path), SourceId = (uint)Marshal.ReadInt32(path, 8) };
                    var name = new SourceName { Header = header };
                    Check(GetSourceName(ref name));
                    if (!name.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase)) continue;
                    header.Type = -3; header.Size = Marshal.SizeOf<ScaleGet>();
                    var get = new ScaleGet { Header = header };
                    Check(GetScale(ref get));
                    var scale = new DisplayScale(header.AdapterId, header.SourceId, get.Minimum, get.Current, get.Maximum);
                    scale.Validate();
                    return scale;
                }
                throw new InvalidOperationException("The capture display has no active Windows display source.");
            }
            finally { Marshal.FreeHGlobal(paths); if (modes != IntPtr.Zero) Marshal.FreeHGlobal(modes); }
        }
        throw new InvalidOperationException("Display topology did not stabilize.");
    }

    internal static void Set(string displayName, DisplayScale original, int relative)
    {
        var current = Read(displayName);
        if (current.AdapterId != original.AdapterId || current.SourceId != original.SourceId
            || current.Minimum != original.Minimum || current.Maximum != original.Maximum)
            throw new InvalidOperationException("The Windows display source changed; refusing to scale a different source.");
        if (relative < current.Minimum || relative > current.Maximum) throw new ArgumentOutOfRangeException(nameof(relative));
        var packet = new ScaleSet { Header = new() { Type = -4, Size = Marshal.SizeOf<ScaleSet>(),
            AdapterId = current.AdapterId, SourceId = current.SourceId }, Relative = relative };
        Check(SetScale(ref packet));
    }

    private static void Check(int status)
    {
        if (status != 0) throw new Win32Exception(status, $"Windows display scaling API failed (error {status}).");
    }
}

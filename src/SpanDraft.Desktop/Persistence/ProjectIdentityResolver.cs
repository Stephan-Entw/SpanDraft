using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SpanDraft.Desktop.Persistence;

/// <summary>Read-only path resolution. A missing file is allowed if its directory can be resolved.</summary>
public sealed class ProjectIdentityResolver : IProjectIdentityResolver
{
    public ProjectIdentity Resolve(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string name = Path.GetFileName(fullPath);
        if (name.Length == 0) throw new IOException($"Expected a project file: {fullPath}");

        string? canonical = ResolveExisting(fullPath);
        if (canonical is null && new FileInfo(fullPath).LinkTarget is not null)
            throw new IOException($"The project path is a symbolic link with a missing target: {fullPath}");

        string parent = Path.GetDirectoryName(canonical ?? fullPath)!;
        string realParent = ResolveExisting(parent) ?? throw new DirectoryNotFoundException(parent);
        if (!File.GetAttributes(realParent).HasFlag(FileAttributes.Directory)) throw new DirectoryNotFoundException(parent);

        bool? linuxCaseSensitive = OperatingSystem.IsLinux() ? Unix.GetLinuxCaseSensitivity(realParent) : null;
        bool? caseSensitive = OperatingSystem.IsMacOS() ? Darwin.GetCaseSensitivity(realParent)
            : OperatingSystem.IsWindows() ? Windows.GetCaseSensitivity(realParent) : linuxCaseSensitive;
        caseSensitive ??= ObserveCaseSensitivity(realParent, canonical);
        if (caseSensitive is null)
            throw new IOException($"Cannot determine the case sensitivity of the project directory: {realParent}");

        bool normalizeName = linuxCaseSensitive is false || OperatingSystem.IsMacOS() && new DriveInfo(realParent).DriveFormat == "apfs";
        return new(Path.Combine(realParent, canonical is null ? name : Path.GetFileName(canonical)), caseSensitive.Value, normalizeName);
    }

    private static string? ResolveExisting(string path)
    {
        if (OperatingSystem.IsWindows()) return Windows.ResolveExisting(path);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) return Unix.ResolveExisting(path);
        throw new PlatformNotSupportedException("Canonical project path resolution is unavailable on this platform.");
    }

    private static bool? ObserveCaseSensitivity(string directory, string? existingPath)
    {
        // Compare directory entries via actual lookups, so an error or a differently cased sibling
        // cannot be mistaken for the same file. No probe file is needed.
        if (existingPath is not null && ObserveEntry(existingPath) is { } fromFile) return fromFile;
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (ObserveEntry(entry) is { } observed) return observed;
        }
        return null;
    }

    private static bool? ObserveEntry(string entry)
    {
        string name = Path.GetFileName(entry);
        int index = name.AsSpan().IndexOfAnyInRange('a', 'z');
        if (index < 0) index = name.AsSpan().IndexOfAnyInRange('A', 'Z');
        if (index < 0 || File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) return null;
        char[] variant = name.ToCharArray();
        variant[index] = (char)(variant[index] ^ 0x20);
        string alternate = Path.Combine(Path.GetDirectoryName(entry)!, new string(variant));
        string? original = ResolveExisting(entry);
        if (original is null) return null;
        string? other = ResolveExisting(alternate);
        return other is null || !StringComparer.Ordinal.Equals(original, other)
            || File.GetAttributes(alternate).HasFlag(FileAttributes.ReparsePoint);
    }

    private static IOException PathError(string path, int error) => new($"{path}: {new Win32Exception(error).Message}");

    private static class Unix
    {
        private const int NoEntry = 2;
        private const int NotDirectory = 20;
        private const int NonBlocking = 0x800;
        private const int OpenPath = 0x200000;
        private const int CloseOnExec = 0x80000;
        private const uint CasefoldFlag = 0x40000000;

        internal static bool? GetLinuxCaseSensitivity(string directory)
        {
            // An unset flag is conclusive only on filesystems that expose their lookup mode through it.
            if (new DriveInfo(directory).DriveFormat is not ("ext2" or "ext3" or "ext4" or "f2fs" or "tmpfs" or "overlay" or "overlayfs"))
                return null;

            int fd = Open(directory, NonBlocking | CloseOnExec);
            if (fd < 0) return null;
            try
            {
                // FS_IOC_GETFLAGS encodes sizeof(long), but the kernel writes an int.
                nuint getFlags = IntPtr.Size == 8 ? 0x80086601u : 0x80046601u;
                return Ioctl(fd, getFlags, out uint flags) == 0 ? (flags & CasefoldFlag) == 0 : null;
            }
            finally { Close(fd); }
        }

        internal static string? ResolveExisting(string path)
        {
            if (OperatingSystem.IsLinux())
            {
                // The kernel's dentry path preserves the actual names on case-insensitive mounts.
                int fd = Open(path, OpenPath | CloseOnExec);
                if (fd < 0) return MissingOrError(path, Marshal.GetLastPInvokeError());
                try
                {
                    return new FileInfo($"/proc/self/fd/{fd}").LinkTarget
                        ?? throw new IOException($"Cannot resolve the project path: {path}");
                }
                finally { Close(fd); }
            }

            IntPtr resolved = RealPath(path, IntPtr.Zero);
            if (resolved == IntPtr.Zero) return MissingOrError(path, Marshal.GetLastPInvokeError());
            try { return Marshal.PtrToStringUTF8(resolved)!; }
            finally { Free(resolved); }
        }

        private static string? MissingOrError(string path, int error) => error is NoEntry or NotDirectory
            ? null : throw PathError(path, error);

        [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);
        [DllImport("libc", EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", EntryPoint = "ioctl")]
        private static extern int Ioctl(int fd, nuint request, out uint flags);
        [DllImport("libc", EntryPoint = "close")]
        private static extern int Close(int fd);
    }

    private static class Darwin
    {
        private const uint VolumeInfo = 0x80000000;
        private const uint VolumeCapabilities = 0x00020000;
        private const uint CaseSensitive = 0x00000100;

        [StructLayout(LayoutKind.Sequential)]
        private struct AttrList
        {
            public ushort BitmapCount, Reserved;
            public uint Common, Volume, Directory, File, Fork;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Capabilities
        {
            public uint Length, Format, Interfaces, Reserved1, Reserved2;
            public uint ValidFormat, ValidInterfaces, ValidReserved1, ValidReserved2;
        }

        internal static bool? GetCaseSensitivity(string directory)
        {
            var attributes = new AttrList { BitmapCount = 5, Volume = VolumeInfo | VolumeCapabilities };
            int size = Marshal.SizeOf<Capabilities>();
            // Volume attributes describe the mount containing the resolved directory.
            if (GetAttrList(directory, ref attributes, out var capabilities, (nuint)size, 0) != 0
                || capabilities.Length != size || (capabilities.ValidFormat & CaseSensitive) == 0)
                return null;
            return (capabilities.Format & CaseSensitive) != 0;
        }

        [DllImport("libc", EntryPoint = "getattrlist")]
        private static extern int GetAttrList([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref AttrList attributes,
            out Capabilities result, nuint size, uint options);
    }

    private static class Windows
    {
        private const uint ReadAttributes = 0x80;
        private const uint BackupSemantics = 0x02000000;
        private const int FileCaseSensitiveInfo = 23;
        private const uint FileCaseSensitiveSearch = 1;

        private static SafeFileHandle Open(string path) => CreateFile(path, ReadAttributes,
            FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, BackupSemantics, IntPtr.Zero);

        internal static string? ResolveExisting(string path)
        {
            using var handle = Open(path);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is 2 or 3) return null;
                throw PathError(path, error);
            }
            var result = new StringBuilder(512);
            uint length = GetFinalPathName(handle, result, (uint)result.Capacity, 0);
            if (length >= result.Capacity)
            {
                result.EnsureCapacity(checked((int)length + 1));
                length = GetFinalPathName(handle, result, (uint)result.Capacity, 0);
            }
            if (length == 0 || length >= result.Capacity) throw PathError(path, Marshal.GetLastPInvokeError());
            string fullPath = result.ToString();
            if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                string unc = @"\\" + fullPath[8..];
                string root = Path.GetPathRoot(unc)!;
                return root.ToUpperInvariant() + unc[root.Length..];
            }
            return fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath[4..] : fullPath;
        }

        internal static bool? GetCaseSensitivity(string directory)
        {
            using var handle = Open(directory);
            if (handle.IsInvalid) throw PathError(directory, Marshal.GetLastPInvokeError());
            if (GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, out uint flags, sizeof(uint))) return (flags & 1) != 0;
            if (GetVolumeInformationByHandle(handle, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, out uint features, IntPtr.Zero, 0)
                && (features & FileCaseSensitiveSearch) == 0) return false;
            return null;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share, IntPtr attributes,
            FileMode disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathName(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out uint info, uint size);
        [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformationByHandle(SafeFileHandle handle, IntPtr name, uint nameLength,
            IntPtr serial, IntPtr maxComponentLength, out uint flags, IntPtr fileSystemName, uint fileSystemNameLength);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using SkiaSharp;
using SpanDraft.Reporting;

namespace SpanDraft.Desktop.Reporting;

/// <summary>The native dialog owns printer settings; GDI receives the same composed pages at 300 dpi.</summary>
internal static class WindowsReportPrinter
{
    [SupportedOSPlatform("windows")]
    internal static async Task PrintAsync(Window owner, ReportDocument report)
    {
        const uint ReturnDc = 0x100, DriverCopies = 0x40000, NoSelection = 4, NoCurrentPage = 0x800000;
        var dialog = new PrintDialog
        {
            Size = (uint)Marshal.SizeOf<PrintDialog>(), Owner = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
            Flags = ReturnDc | DriverCopies | NoSelection | NoCurrentPage,
            MinPage = 1, MaxPage = (uint)report.Pages.Count, Copies = 1, MaxRanges = 16, StartPage = uint.MaxValue,
            Ranges = Marshal.AllocHGlobal(Marshal.SizeOf<PageRange>() * 16)
        };
        try
        {
            int result = PrintDlgExW(ref dialog);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            if (dialog.Action != 1) return; // Cancel and Apply do not submit a job.
            var pages = Enumerable.Range(0, report.Pages.Count).ToArray();
            if ((dialog.Flags & 2) != 0)
            {
                var ranges = Enumerable.Range(0, (int)dialog.RangeCount).Select(i =>
                    Marshal.PtrToStructure<PageRange>(IntPtr.Add(dialog.Ranges, i * Marshal.SizeOf<PageRange>()))).ToArray();
                pages = pages.Where(i => ranges.Any(r => i + 1 >= r.From && i + 1 <= r.To)).ToArray();
            }
            if (pages.Length == 0) return;
            var dc = dialog.Dc;
            await Task.Run(() => Submit(dc, report, pages));
        }
        finally
        {
            if (dialog.Dc != IntPtr.Zero) DeleteDC(dialog.Dc);
            if (dialog.DevMode != IntPtr.Zero) GlobalFree(dialog.DevMode);
            if (dialog.DevNames != IntPtr.Zero) GlobalFree(dialog.DevNames);
            Marshal.FreeHGlobal(dialog.Ranges);
        }
    }

    private static void Submit(IntPtr dc, ReportDocument report, int[] pages)
    {
        var info = new DocumentInfo { Size = Marshal.SizeOf<DocumentInfo>(), Name = report.Data.Title };
        Check(StartDocW(dc, ref info));
        bool completed = false;
        try
        {
            var destination = Destination(GetDeviceCaps(dc, 8), GetDeviceCaps(dc, 10),
                GetDeviceCaps(dc, 88), GetDeviceCaps(dc, 90));
            foreach (int page in pages)
            {
                using var bitmap = report.RenderPage(page);
                var header = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = bitmap.Width,
                    Height = -bitmap.Height, Planes = 1, BitCount = 32, ImageSize = (uint)bitmap.ByteCount };
                Check(StartPage(dc));
                SetStretchBltMode(dc, 4);
                SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
                Check(StretchDIBits(dc, (int)destination.Left, (int)destination.Top, (int)destination.Width, (int)destination.Height,
                    0, 0, bitmap.Width, bitmap.Height, bitmap.GetPixels(), ref header, 0, 0x00CC0020));
                Check(EndPage(dc));
            }
            Check(EndDoc(dc));
            completed = true;
        }
        finally { if (!completed) AbortDoc(dc); }
    }

    // Account for devices with different horizontal and vertical pixel densities.
    internal static SKRect Destination(int width, int height, int dpiX, int dpiY)
    {
        if (width <= 0 || height <= 0 || dpiX <= 0 || dpiY <= 0) throw new InvalidOperationException("Invalid printer dimensions.");
        double pageWidth = ReportDocument.PageWidth / 72 * dpiX, pageHeight = ReportDocument.PageHeight / 72 * dpiY;
        double scale = Math.Min(width / pageWidth, height / pageHeight);
        int w = Math.Max(1, (int)Math.Floor(pageWidth * scale)), h = Math.Max(1, (int)Math.Floor(pageHeight * scale));
        return new((width - w) / 2, (height - h) / 2, (width - w) / 2 + w, (height - h) / 2 + h);
    }

    private static void Check(int result) { if (result <= 0) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintDialog
    {
        public uint Size; public IntPtr Owner, DevMode, DevNames, Dc;
        public uint Flags, Flags2, ExclusionFlags, RangeCount, MaxRanges;
        public IntPtr Ranges; public uint MinPage, MaxPage, Copies;
        public IntPtr Instance, Template, Callback; public uint PropertyPages;
        public IntPtr Pages; public uint StartPage, Action;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PageRange { public uint From, To; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocumentInfo
    {
        public int Size; [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        public IntPtr Output, DataType; public uint Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern int PrintDlgExW(ref PrintDialog dialog);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern int StartDocW(IntPtr dc, ref DocumentInfo info);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int EndPage(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int AbortDoc(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int StretchDIBits(IntPtr dc, int x, int y, int width, int height,
        int sx, int sy, int sw, int sh, IntPtr bits, ref BitmapInfo info, uint usage, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}

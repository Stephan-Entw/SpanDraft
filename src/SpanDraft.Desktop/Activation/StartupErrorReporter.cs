using System.Diagnostics;
using System.Runtime.InteropServices;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.Activation;

/// <summary>Visible failure of a Secondary start before any Avalonia or settings exist; uses the system UI culture.</summary>
internal static class StartupErrorReporter
{
    public static void Report(InstanceOutcomeKind kind)
    {
        string message = kind == InstanceOutcomeKind.Rejected ? Strings.InstanceRequestRejected : Strings.InstanceNotReachable;
        Console.Error.WriteLine(message);
        try
        {
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, "SpanDraft", 0x10);
            else if (OperatingSystem.IsMacOS())
            {
                string escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
                using var process = Process.Start(new ProcessStartInfo("osascript")
                {
                    ArgumentList = { "-e", $"display alert \"SpanDraft\" message \"{escaped}\" as critical" },
                    UseShellExecute = false
                });
                process?.WaitForExit();
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

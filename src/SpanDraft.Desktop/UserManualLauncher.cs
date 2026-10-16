namespace SpanDraft.Desktop;

/// <summary>Opens the bundled offline manual through the owning Avalonia window's launcher.</summary>
public sealed class UserManualLauncher(Func<Uri, Task<bool>> launch, string? baseDirectory = null)
{
    public async Task<bool> OpenAsync()
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Help", "index.html"));
            return File.Exists(path) && await launch(new Uri(path));
        }
        catch (Exception) { return false; }
    }
}

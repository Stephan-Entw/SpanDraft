using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
string help = Path.Combine(root, "help");
string[] names = ["beispiel-balken", "lagerarten", "lasten-vorzeichen", "koordinaten", "querschnitt-achsen", "ergebnisverlaeufe"];
try
{
    await Run(root, "dotnet", "run", "--project", Path.Combine(root, "tools", "SpanDraft.HelpAssets"),
        "--configuration", "Release", "--", Path.Combine(help, "docs", "assets"));
    foreach (var name in names) RequireFile(Path.Combine(help, "docs", "assets", name + ".png"));
    await Run(help, "zensical", "build", "--clean", "--strict");
    string site = Path.Combine(help, "site"); RequireFile(Path.Combine(site, "index.html"));
    foreach (string file in Directory.EnumerateFiles(Path.Combine(help, "docs"), "*.md", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(site, "*.html", SearchOption.AllDirectories)))
    {
        string content = File.ReadAllText(file);
        foreach (var name in names)
            if (content.Contains(name + ".svg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Obsolete SVG reference in {file}: {name}.svg");
    }
    foreach (string file in Directory.EnumerateFiles(site, "*", SearchOption.AllDirectories)
        .Where(f => Path.GetExtension(f) is ".html" or ".css"))
    {
        string content = File.ReadAllText(file);
        // Check resource loading, not ordinary outgoing hyperlinks or bundled license text.
        if (Regex.IsMatch(content, "(?:src|href)\\s*=\\s*[\"'](?:https?:)?//[^\"']*(?:fonts\\.(?:googleapis|gstatic)\\.com|iframe-worker)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(content, "(?:url\\(|@import\\s+)[\\s\"']*(?:https?:)?//", RegexOptions.IgnoreCase))
            throw new InvalidOperationException($"External runtime font/worker/style in {file}");
        if (Path.GetExtension(file) == ".html")
        {
            foreach (Match match in Regex.Matches(content, "<(?:script|link|img)\\b[^>]*?\\b(?:src|href)=[\"']([^\"']+)", RegexOptions.IgnoreCase))
            {
                string reference = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
                if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (reference.StartsWith("//") || Uri.TryCreate(reference, UriKind.Absolute, out _))
                    throw new InvalidOperationException($"Nonlocal runtime resource in {file}: {reference}");
                string relative = Uri.UnescapeDataString(reference.Split('?', '#')[0]);
                if (relative.Length > 0) RequireFile(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, relative)));
            }
        }
    }
    foreach (var name in names) RequireFile(Path.Combine(site, "assets", name + ".png"));
    RequireFile(Path.Combine(site, "js", "iframe-worker-shim.js"));
    RequireFile(Path.Combine(site, "js", "iframe-worker-LICENSE.txt"));
    Console.WriteLine("Offline help validated: six PNGs, HTML, local runtime resources, no obsolete SVG references.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Help build failed: {error.Message}\nInstall Python 3.11+ and run pip install -r help/requirements.txt in your activated environment.");
    return 1;
}
static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
static void RequireFile(string path)
{
    if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidOperationException($"Missing or empty help file: {path}");
}
static async Task Run(string directory, string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}.");
}

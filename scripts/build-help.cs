using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--no-regenerate-assets"))
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/build-help.cs [-- --no-regenerate-assets]");
    return 1;
}

string root = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
string help = Path.Combine(root, "help"), site = Path.Combine(help, "site"), work = Path.Combine(help, ".build");
string[] shared = ["koordinaten", "querschnitt-achsen", "lager-einspannung", "lager-festlager", "lager-loslager",
    "punktkraft-vorzeichen", "punktmoment-vorzeichen", "streckenlast-vorzeichen"];
string[] localized = ["beispiel-lagerung", "beispiel-belastung", "diagramm-durchbiegung", "diagramm-querkraft", "diagramm-biegemoment"];
string[] names = [.. shared, .. localized];
try
{
    string zensical = ResolveZensical(help);
    await Run(root, "dotnet", "run", "--project", Path.Combine(root, "tools", "SpanDraft.HelpAssets"),
        "--configuration", "Release", "--", args.Length == 0 ? "--generate" : "--check", Path.Combine(help, "assets"));
    RequirePngAssets(Path.Combine(help, "assets", "shared"), shared);
    foreach (string language in new[] { "de", "en" }) RequirePngAssets(Path.Combine(help, "assets", language), localized);
    if (Directory.Exists(site)) Directory.Delete(site, true);
    if (Directory.Exists(work)) Directory.Delete(work, true);
    Directory.CreateDirectory(work);
    CopyTree(Path.Combine(help, "assets"), Path.Combine(site, "assets"));
    foreach (string language in new[] { "de", "en" })
    {
        string source = Path.Combine(help, "docs", language), staged = Path.Combine(work, language, "docs");
        string output = Path.Combine(site, language);
        string[] pages = Directory.GetFiles(source, "*.md", SearchOption.AllDirectories);
        if (pages.Length != 11) throw new InvalidOperationException($"Expected eleven {language} pages, found {pages.Length}.");
        foreach (string page in pages) ValidateMath(File.ReadAllText(page), page);
        CopyTree(source, staged);
        foreach (string folder in new[] { "js", "stylesheets" }) CopyTree(Path.Combine(help, "shared", folder), Path.Combine(staged, folder));
        foreach (string name in names)
        {
            string asset = Path.Combine(help, "assets", shared.Contains(name) ? "shared" : language, name + ".png");
            Directory.CreateDirectory(Path.Combine(staged, "assets"));
            File.Copy(asset, Path.Combine(staged, "assets", name + ".png"));
        }
        string config = File.ReadAllText(Path.Combine(help, "zensical.toml"))
            .Replace("{{title}}", language == "de" ? "SpanDraft – Benutzerhandbuch" : "SpanDraft – User Manual")
            .Replace("{{docs}}", $".build/{language}/docs").Replace("{{site}}", $"site/{language}")
            .Replace("{{language}}", language).Replace("{{navigation}}", File.ReadAllText(Path.Combine(help, $"nav.{language}.toml")));
        // Keep the config in help/: Zensical resolves all paths against its config directory.
        string configPath = Path.Combine(help, $".zensical-{language}.toml");
        try
        {
            File.WriteAllText(configPath, config);
            await Run(help, zensical, "build", "--clean", "--strict", "--config-file", configPath);
        }
        finally { File.Delete(configPath); }
        foreach (string page in pages)
            RequireFile(Path.Combine(output, Path.ChangeExtension(Path.GetRelativePath(source, page), ".html")));
        foreach (string html in Directory.GetFiles(output, "*.html", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(html);
            // Only technical PNGs move; the theme's own assets and offline search stay language-local.
            content = Regex.Replace(content, "(?<prefix>\\b(?:src|href)=[\"'])(?<url>[^\"']+\\.png)(?<suffix>[\"'])", match =>
            {
                string url = WebUtility.HtmlDecode(match.Groups["url"].Value);
                string name = Path.GetFileNameWithoutExtension(url);
                if (!names.Contains(name)) return match.Value;
                string oldPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(html)!, Uri.UnescapeDataString(url)));
                if (oldPath != Path.Combine(output, "assets", name + ".png")) return match.Value;
                string asset = Path.Combine(site, "assets", shared.Contains(name) ? "shared" : language, name + ".png");
                return match.Groups["prefix"].Value + Path.GetRelativePath(Path.GetDirectoryName(html)!, asset).Replace('\\', '/') + match.Groups["suffix"].Value;
            });
            File.WriteAllText(html, content);
        }
        foreach (string name in names) File.Delete(Path.Combine(output, "assets", name + ".png"));
        foreach (string resource in new[] { "spandraft-help.js", "iframe-worker-shim.js", "iframe-worker-LICENSE.txt" })
            RequireFile(Path.Combine(output, "js", resource));
    }
    ValidateSite(site, names);
    Console.WriteLine("Offline help validated: DE/EN, eleven pages each, eighteen shared/localized PNGs, links, MathML and local runtime resources.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Help build failed: {error.Message}");
    return 1;
}
static void ValidateSite(string site, string[] names)
{
    foreach (string file in Directory.EnumerateFiles(site, "*", SearchOption.AllDirectories)
        .Where(f => Path.GetExtension(f) is ".html" or ".css"))
    {
        string content = File.ReadAllText(file);
        foreach (string name in names)
            if (content.Contains(name + ".svg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Obsolete SVG reference in {file}: {name}.svg");
        string styles = Path.GetExtension(file) == ".css" ? content : string.Join("\n",
            Regex.Matches(content, "<style\\b[^>]*>(.*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value));
        foreach (Match match in Regex.Matches(styles, "url\\(\\s*[\"']?(?<url>[^)\\s\"']+)|@import\\s+[\"'](?<url>[^\"']+)", RegexOptions.IgnoreCase))
            ValidateReference(file, match.Groups["url"].Value, site, runtime: true);
        if (Path.GetExtension(file) != ".html") continue;
        ValidateMath(content, file);
        foreach (Match tag in Regex.Matches(content, "<(?<tag>a|script|link|img|iframe|source)\\b[^>]*>", RegexOptions.IgnoreCase))
            foreach (Match reference in Regex.Matches(tag.Value, "\\b(?:src|href)=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase))
                ValidateReference(file, reference.Groups["url"].Value, site, runtime: !tag.Groups["tag"].Value.Equals("a", StringComparison.OrdinalIgnoreCase));
    }
}
static void ValidateReference(string file, string value, string site, bool runtime)
{
    string reference = WebUtility.HtmlDecode(value);
    if (runtime && reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
    if (reference.StartsWith("//") || Uri.TryCreate(reference, UriKind.Absolute, out _))
    {
        if (runtime) throw new InvalidOperationException($"Nonlocal runtime resource in {file}: {reference}");
        return;
    }
    string relative = Uri.UnescapeDataString(reference.Split('?', '#')[0]);
    string target = relative.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, relative));
    if (!Path.GetRelativePath(site, target).StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    {
        if (Directory.Exists(target)) throw new InvalidOperationException($"Directory reference in {file}: {reference} -> {target}");
        RequireFile(target);
    }
    else throw new InvalidOperationException($"Reference escapes offline help in {file}: {reference}");
    if (!runtime && reference.Contains('#') && Path.GetExtension(target) == ".html")
    {
        string fragment = Uri.UnescapeDataString(reference[(reference.IndexOf('#') + 1)..]);
        if (fragment.Length != 0 && !Regex.Matches(File.ReadAllText(target), "\\bid=[\"']([^\"']*)[\"']")
            .Any(id => WebUtility.HtmlDecode(id.Groups[1].Value) == fragment))
            throw new InvalidOperationException($"Missing fragment in {file}: {reference}");
    }
}
static void ValidateMath(string content, string file)
{
    foreach (Match math in Regex.Matches(content, "<math\\b.*?</math>", RegexOptions.Singleline))
    {
        var element = XElement.Parse(math.Value);
        if (element.Name != XName.Get("math", "http://www.w3.org/1998/Math/MathML")
            || string.IsNullOrWhiteSpace((string?)element.Attribute("aria-label")))
            throw new InvalidOperationException($"Invalid or inaccessible MathML in {file}");
    }
    if (Regex.Matches(content, "<math\\b").Count != Regex.Matches(content, "</math>").Count)
        throw new InvalidOperationException($"Unclosed MathML in {file}");
}
static void CopyTree(string source, string destination)
{
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target);
    }
}
static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
static string ResolveZensical(string help)
{
    string executable = OperatingSystem.IsWindows() ? "zensical.exe" : "zensical";
    string local = Path.Combine(help, ".venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", executable);
    if (File.Exists(local)) return local;
    foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(directory)) continue;
        string candidate = Path.GetFullPath(Path.Combine(directory, executable));
        if (File.Exists(candidate)) return candidate;
    }
    throw new InvalidOperationException($"Zensical was not found at {local} or on PATH. " +
        "Install Python 3.11+ and run pip install -r help/requirements.txt in your Python environment.");
}
static void RequireFile(string path)
{
    if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidOperationException($"Missing or empty help file: {path}");
}
static void RequirePngAssets(string directory, string[] names)
{
    foreach (string name in names) RequireFile(Path.Combine(directory, name + ".png"));
    foreach (string file in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
        if (!names.Contains(Path.GetFileNameWithoutExtension(file), StringComparer.Ordinal))
            throw new InvalidOperationException($"Unexpected help PNG: {file}");
}
static async Task Run(string directory, string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}.");
}

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--check"))
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/generate-third-party-notices.cs [-- --check]");
    return 1;
}

var check = args.Length == 1;
var repositoryRoot = Path.GetFullPath(Path.Combine(GetScriptDirectory(), ".."));
var project = Path.Combine(repositoryRoot, "src", "SpanDraft.Desktop", "SpanDraft.Desktop.csproj");
var output = Path.Combine(repositoryRoot, "THIRD_PARTY_NOTICES.txt");
// Keep the temporary output on the same filesystem for the final atomic replacement.
var temporaryOutput = Path.Combine(repositoryRoot, $".third-party-notices-{Guid.NewGuid():N}.tmp");

try
{
    await RunDotnet(repositoryRoot, "tool", "restore", "--tool-manifest",
        Path.Combine(repositoryRoot, ".config", "dotnet-tools.json"));
    await RunDotnet(repositoryRoot, "restore", project, "--force");

    var packages = ReadRuntimePackages(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"));
    await RunDotnet(repositoryRoot, "tool", "run", "thirdlicense", "--project", project, "--output", temporaryOutput);

    var notices = ReadNotices(await File.ReadAllTextAsync(temporaryOutput));
    var missing = packages.Where(package => !notices.ContainsKey(package)).ToArray();
    if (missing.Length != 0)
    {
        throw new InvalidOperationException($"Missing runtime package notices: {string.Join(", ", missing)}");
    }

    var orderedPackages = packages
        .OrderBy(package => package.Split('/')[0], StringComparer.OrdinalIgnoreCase)
        .ThenBy(package => package.Split('/')[1], StringComparer.OrdinalIgnoreCase);
    var content = string.Join("\n\n\n", orderedPackages.Select(package => notices[package].TrimEnd('\n'))) + "\n";
    var bytes = new UTF8Encoding(false).GetBytes(content);
    var existing = File.Exists(output) ? await File.ReadAllBytesAsync(output) : null;
    var current = existing != null && bytes.AsSpan().SequenceEqual(existing);
    if (check)
    {
        if (!current)
        {
            Console.Error.WriteLine("THIRD_PARTY_NOTICES.txt is missing or outdated. Run the script without --check and commit the result.");
            return 1;
        }

        Console.WriteLine($"THIRD_PARTY_NOTICES.txt is current ({packages.Count} runtime packages).");
    }
    else if (current)
    {
        Console.WriteLine($"THIRD_PARTY_NOTICES.txt is unchanged ({packages.Count} runtime packages).");
    }
    else
    {
        await File.WriteAllBytesAsync(temporaryOutput, bytes);
        File.Move(temporaryOutput, output, overwrite: true);
        Console.WriteLine($"Updated THIRD_PARTY_NOTICES.txt ({packages.Count} runtime packages).");
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Notice generation failed: {exception.Message}");
    return 1;
}
finally
{
    File.Delete(temporaryOutput);
}

static string GetScriptDirectory([CallerFilePath] string sourcePath = "") => Path.GetDirectoryName(sourcePath)!;

static async Task RunDotnet(string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
    };
    // ThirdLicense parses dotnet list's human-readable output.
    startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start dotnet.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet {arguments[0]} {arguments[1]} failed (exit code {process.ExitCode}).");
    }
}

static SortedSet<string> ReadRuntimePackages(string assetsPath)
{
    using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
    var packages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var target in assets.RootElement.GetProperty("targets").EnumerateObject())
    {
        // Use the portable graph so notices include native assets for every desktop platform.
        if (target.Name.Contains('/'))
        {
            continue;
        }

        foreach (var package in target.Value.EnumerateObject())
        {
            if (package.Value.GetProperty("type").GetString() == "package" && HasRuntimeAssets(package.Value))
            {
                packages.Add(NormalizeIdentity(package.Name));
            }
        }
    }

    if (packages.Count == 0)
    {
        throw new InvalidOperationException("The Desktop assets graph contains no runtime packages.");
    }

    return packages;
}

static bool HasRuntimeAssets(JsonElement package)
{
    foreach (var group in new[] { "runtime", "native", "resource", "runtimeTargets" })
    {
        if (!package.TryGetProperty(group, out var entries))
        {
            continue;
        }

        foreach (var asset in entries.EnumerateObject())
        {
            if (asset.Name == "_._" || asset.Name.EndsWith("/_._", StringComparison.Ordinal))
            {
                continue;
            }

            if (group != "runtimeTargets" ||
                asset.Value.GetProperty("assetType").GetString() is "runtime" or "native" or "resource")
            {
                return true;
            }
        }
    }

    return false;
}

static Dictionary<string, string> ReadNotices(string text)
{
    text = text.Replace("\r\n", "\n").Replace('\r', '\n');
    var headers = Regex.Matches(text, @"^License notice for (?<id>\S+) \(v(?<version>[^)\n]+)\)\n-+\n", RegexOptions.Multiline);
    var notices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < headers.Count; index++)
    {
        var header = headers[index];
        var end = index + 1 < headers.Count ? headers[index + 1].Index : text.Length;
        var identity = NormalizeIdentity($"{header.Groups["id"].Value}/{header.Groups["version"].Value}");
        notices.Add(identity, text[header.Index..end]);
    }

    return notices;
}

// NuGet package identities ignore SemVer build metadata, which ThirdLicense may print.
static string NormalizeIdentity(string identity) => identity.Split('+', 2)[0];

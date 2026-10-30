using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--check"))
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/update-icons.cs [-- --check]");
    return 1;
}

// Pin the upstream paint fix and a renderer that honors SVG text baselines.
const string revision = "1beb52777c564c218f460ee0510f73cba21c6d69";
const string archiveHash = "9D1352AC988D81CA870CB54238477E014D56B196EB13941C64185866D869799C";
const string svgSkiaVersion = "5.1.1";
// The SVG's STIX Two Text needs the same font on every build host (SIL OFL 1.1).
const string fontUrl = "https://raw.githubusercontent.com/stipub/stixfonts/37308d44583ef882dc8b544aaa2e80ac8c406ebf/fonts/static_ttf/STIXTwoText-Regular.ttf";
const string fontHash = "5B7503D88E9F88DB351A6E879B468925B01D9BAE2FC29806F6A1953D28E53116";
var root = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
var icons = Path.Combine(root, "src", "SpanDraft.Desktop", "Assets", "Icons");
var cache = Path.Combine(root, ".artifacts", "icon-converter", revision, "svg-skia-" + svgSkiaVersion, "stix-2.13");
var fontPath = Path.Combine(cache, "STIXTwoText-Regular.ttf");
var source = Path.Combine(cache, "SvgToXaml-" + revision);
var converter = Path.Combine(source, "src", "svgxaml", "bin", "Release", "net8.0", "svgxaml.dll");
var temporary = Path.Combine(Path.GetTempPath(), "spandraft-icons-" + Guid.NewGuid().ToString("N"));
XNamespace a = "https://github.com/avaloniaui";
XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

try
{
    Directory.CreateDirectory(temporary);
    if (!File.Exists(converter))
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var bytes = await client.GetByteArrayAsync($"https://codeload.github.com/wieslawsoltes/SvgToXaml/zip/{revision}");
        if (Convert.ToHexString(SHA256.HashData(bytes)) != archiveHash)
            throw new InvalidOperationException("SvgToXaml source archive checksum mismatch.");
        Directory.CreateDirectory(cache);
        var fontBytes = await client.GetByteArrayAsync(fontUrl);
        if (Convert.ToHexString(SHA256.HashData(fontBytes)) != fontHash)
            throw new InvalidOperationException("STIX Two Text checksum mismatch.");
        await File.WriteAllBytesAsync(fontPath, fontBytes);
        using var archive = new ZipArchive(new MemoryStream(bytes));
        archive.ExtractToDirectory(cache, overwriteFiles: true);
        // The converter's original Svg.Skia 4.1.0 ignores dominant-baseline.
        var rendererPropsPath = Path.Combine(source, "build", "Svg.Skia.props");
        var rendererProps = XDocument.Load(rendererPropsPath);
        rendererProps.Descendants().Single(e => e.Name.LocalName == "PackageReference" && (string?)e.Attribute("Include") == "Svg.Skia")
            .SetAttributeValue("Version", svgSkiaVersion);
        rendererProps.Save(rendererPropsPath);
        // Match the renderer's added optional command fields without changing the drawing logic.
        var drawingGroupPath = Path.Combine(source, "src", "SvgToXaml.Model", "Drawing", "DrawingGroup.cs");
        var drawingGroup = await File.ReadAllTextAsync(drawingGroupPath);
        drawingGroup = drawingGroup
            .Replace("SaveLayerCanvasCommand(_, var skPaint)", "SaveLayerCanvasCommand(_, var skPaint, _)")
            .Replace("DrawTextCanvasCommand(var text, var x, var y, var skPaint)", "DrawTextCanvasCommand(var text, var x, var y, var skPaint, _, _)")
            .Replace("DrawTextOnPathCanvasCommand(var text, var skPath, var hOffset, var vOffset, var skPaint)", "DrawTextOnPathCanvasCommand(var text, var skPath, var hOffset, var vOffset, var skPaint, _, _)")
            .Replace("DrawImageCanvasCommand(var skImage, var skRect, var dest, var skPaint)", "DrawImageCanvasCommand(var skImage, var skRect, var dest, var skPaint, _)");
        await File.WriteAllTextAsync(drawingGroupPath, drawingGroup);
        // Resolve both SVG layout and glyph outlines from the pinned font, never a system fallback.
        const string typefaceProviders = "new Svg.Skia.TypefaceProviders.ITypefaceProvider[] { new Svg.Skia.TypefaceProviders.CustomTypefaceProvider(System.Environment.GetEnvironmentVariable(\"SPANDRAFT_ICON_FONT\")!) }";
        var factoryPath = Path.Combine(source, "src", "SvgToXaml.Model", "Factory.cs");
        var factory = (await File.ReadAllTextAsync(factoryPath)).Replace("new Svg.Skia.SKSvgSettings()",
            "new Svg.Skia.SKSvgSettings { TypefaceProviders = " + typefaceProviders + " }");
        await File.WriteAllTextAsync(factoryPath, factory);
        var converterPath = Path.Combine(source, "src", "SvgToXaml.Converter", "SvgToXamlConverter.cs");
        var converterCode = (await File.ReadAllTextAsync(converterPath)).Replace("if (skSvg.FromSvg(inputItem.Content) is null)",
            "skSvg.Settings.TypefaceProviders = " + typefaceProviders + ";\n                if (skSvg.FromSvg(inputItem.Content) is null)");
        await File.WriteAllTextAsync(converterPath, converterCode);
        await Dotnet("build", Path.Combine(source, "src", "svgxaml", "svgxaml.csproj"),
            "--configuration", "Release", "--disable-build-servers", "--verbosity", "quiet",
            "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false");
        if (!File.Exists(converter)) throw new InvalidOperationException("SvgToXaml build produced no converter.");
    }

    var outputs = new List<(string Path, byte[] Bytes)>();
    foreach (var family in new[] { "Toolbar", "Fluent" })
    {
        var files = Directory.GetFiles(Path.Combine(icons, family), "*.svg").Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidOperationException($"No {family} SVG sources found.");
        var raw = Path.Combine(temporary, family + ".axaml");
        await Dotnet(converter, Path.Combine(icons, family), raw);
        // SvgToXaml can report conversion failures without returning a failing exit code.
        var converted = XDocument.Load(raw).Root!.Element(a + "Style")?.Element(a + "Style.Resources")?.Elements().ToArray()
            ?? throw new InvalidOperationException($"Missing {family} converter resources.");
        if (converted.Length != files.Length)
            throw new InvalidOperationException($"Incomplete {family} conversion: expected {files.Length} resources, got {converted.Length}.");
        var dictionary = new XElement(a + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
            new XComment($" Generated by scripts/update-icons.cs using SvgToXaml {revision}, Svg.Skia {svgSkiaVersion}. Do not edit. "));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var drawing = new XElement(converted.Single(e => ((string?)e.Attribute(x + "Key"))?.TrimStart('_') == name.Replace('-', '_')));
            drawing.Attribute(x + "Key")!.Remove();
            string shortName = family == "Fluent" ? name.Replace("ic_fluent_", "").Replace("_16_regular", "").Replace("_20_regular", "").Replace("_24_regular", "") : name;
            string key = (family == "Toolbar" ? "ToolbarIcon." : "FluentIcon.") +
                string.Concat(shortName.Split('-', '_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate resource key: {key}");
            if (family == "Toolbar")
            {
                if (drawing.Name != a + "DrawingGroup" || !drawing.Descendants(a + "Pen").Any())
                    throw new InvalidOperationException($"Missing toolbar paint in {name}.");
                dictionary.Add(new XElement(a + "DrawingImage", new XAttribute(x + "Key", key),
                    new XElement(a + "DrawingGroup",
                        // DrawingImage otherwise scales the painted bounds instead of the SVG artboard.
                        new XElement(a + "GeometryDrawing", new XAttribute("Brush", "Transparent"),
                            new XAttribute("Geometry", "M0,0 H48 V48 H0 Z")), drawing)));
            }
            else
            {
                // Current Fluent sources convert to one opaque filled geometry without transforms.
                var path = drawing;
                while (path.Name == a + "DrawingGroup" && !path.HasAttributes && path.Elements().Count() == 1)
                    path = path.Elements().Single();
                if (path.Name != a + "GeometryDrawing" ||
                    path.HasElements || path.Attributes().Any(p => p.Name != "Geometry" && p.Name != "Brush") ||
                    (string?)path.Attribute("Brush") is not { Length: 9 } brush || !brush.StartsWith("#FF", StringComparison.Ordinal) ||
                    !uint.TryParse(brush.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ||
                    string.IsNullOrWhiteSpace((string?)path.Attribute("Geometry")))
                    throw new InvalidOperationException($"Unsupported monochrome converter output for {name}; preserve its drawing semantics before adding it.");
                dictionary.Add(new XElement(a + "StreamGeometry", new XAttribute(x + "Key", key), (string)path.Attribute("Geometry")!));
            }
        }
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, NewLineChars = "\n" }))
            dictionary.Save(writer);
        outputs.Add((Path.Combine(icons, "Generated", family + "Icons.axaml"), new UTF8Encoding(false).GetBytes(text + "\n")));
    }

    bool outdated = false;
    foreach (var output in outputs)
    {
        if (File.Exists(output.Path) && (await File.ReadAllBytesAsync(output.Path)).AsSpan().SequenceEqual(output.Bytes)) continue;
        if (args.Length == 1)
        {
            Console.Error.WriteLine($"Outdated or missing: {Path.GetRelativePath(root, output.Path)}");
            outdated = true;
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!);
            await File.WriteAllBytesAsync(output.Path, output.Bytes);
            Console.WriteLine($"Updated {Path.GetRelativePath(root, output.Path)}");
        }
    }
    if (outdated) return 1;
    Console.WriteLine("Native icon resources are current.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Icon generation failed: {error.Message}");
    return 1;
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

async Task Dotnet(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false };
    // Upstream sorts filenames using the current culture; keep ordering stable on every OS.
    start.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
    start.Environment["DOTNET_ROLL_FORWARD"] = "Major";
    start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
    start.Environment["SPANDRAFT_ICON_FONT"] = fontPath;
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException($"Icon converter failed (exit code {process.ExitCode}).");
}

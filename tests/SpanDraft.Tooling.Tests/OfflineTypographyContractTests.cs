using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SpanDraft.Tests;

public sealed class OfflineTypographyContractTests
{
    private static string Repository([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));

    [Fact]
    public void HelpFacesUseOnlyTheRequiredCanonicalBundledFonts()
    {
        var expected = new[]
        {
            ("IBM Plex Sans", "normal", "400", "IBMPlexSans-Regular.ttf"),
            ("IBM Plex Sans", "italic", "400", "IBMPlexSans-Italic.ttf"),
            ("IBM Plex Sans", "normal", "600", "IBMPlexSans-SemiBold.ttf"),
            ("IBM Plex Sans", "italic", "600", "IBMPlexSans-SemiBoldItalic.ttf"),
            ("IBM Plex Mono", "normal", "400", "IBMPlexMono-Regular.ttf")
        };
        string css = File.ReadAllText(Path.Combine(Repository(), "help", "shared", "stylesheets", "fonts.css"));
        var faces = Regex.Matches(css, @"@font-face\s*\{([^}]+)\}");
        Assert.Equal(expected.Length, faces.Count);
        foreach (var (family, style, weight, file) in expected)
        {
            var face = Assert.Single(faces.Cast<Match>(), match => Property(match.Value, "src").Contains(file, StringComparison.Ordinal));
            Assert.Equal($"\"{family}\"", Property(face.Value, "font-family"));
            Assert.Equal(style, Property(face.Value, "font-style"));
            Assert.Equal(weight, Property(face.Value, "font-weight"));
            Assert.Equal($"url(\"../../assets/fonts/{file}\") format(\"truetype\")", Property(face.Value, "src"));
            Assert.True(new FileInfo(Path.Combine(Repository(), "src", "SpanDraft.Desktop", "Assets", "Fonts", file)).Length > 0);
        }
        Assert.Equal("\"IBM Plex Sans\"", Property(css, "--md-text-font"));
        Assert.Equal("\"IBM Plex Mono\"", Property(css, "--md-code-font"));
        Assert.DoesNotMatch(@"(?i)@import\b|\blocal\s*\(|https?://|fonts\.google", css);
        Assert.Contains("font = false", File.ReadAllText(Path.Combine(Repository(), "help", "zensical.toml")));
        string main = File.ReadAllText(Path.Combine(Repository(), "help", "shared", "overrides", "main.html"));
        Assert.Matches(@"(?s)\{% block fonts %\}.*stylesheets/fonts\.css.*\{% endblock %\}", main);
        Assert.Contains("stylesheets/fonts.css", File.ReadAllText(Path.Combine(Repository(), "help", "shared", "overrides", "404.html")));
    }

    [Fact]
    public void LicenseAndOwnedHelpSourcesDoNotDeclareSystemFontFamilies()
    {
        var license = XDocument.Load(Path.Combine(Repository(), "src", "SpanDraft.Desktop", "LicenseWindow.axaml"));
        var families = license.Descendants().Attributes("FontFamily").Select(attribute => attribute.Value).ToArray();
        Assert.Equal(new[] { "{DynamicResource MonospaceFont}" }, families);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Repository(), "help", "shared"), "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".css" or ".html"))
        {
            string content = File.ReadAllText(file);
            foreach (Match declaration in Regex.Matches(content, @"(?:font-family|--md-(?:text|code)-font)\s*:\s*([^;}]+)"))
                Assert.Contains(declaration.Groups[1].Value.Trim(), new[] { "\"IBM Plex Sans\"", "\"IBM Plex Mono\"", "var(--md-text-font)", "var(--md-code-font)", "inherit" });
            Assert.DoesNotMatch(@"(?i)(?<![\w-])font\s*:(?!\s*inherit\s*[;}])", content);
        }
        var tokens = XDocument.Load(Path.Combine(Repository(), "src", "SpanDraft.Desktop", "Styles", "DesignTokens.axaml"));
        Assert.Equal("avares://SpanDraft.Desktop/Assets/Fonts#IBM Plex Mono", Assert.Single(tokens.Descendants(),
            element => (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MonospaceFont").Value);
    }

    private static string Property(string css, string name) =>
        Regex.Match(css, Regex.Escape(name) + @"\s*:\s*([^;}]+)").Groups[1].Value.Trim();
}

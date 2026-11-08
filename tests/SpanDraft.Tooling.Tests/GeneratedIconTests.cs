using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Platform;
using Avalonia.Styling;
using Xunit;
using Path = System.IO.Path;

namespace SpanDraft.Tests;

// AssetLoader reads Avalonia resources from the built application assembly.
[Collection("Avalonia")]
public sealed class GeneratedIconTests : DesktopIconTestSupport
{

    private static string Repository([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));

    [Fact]
    public void ToolbarSourcesHaveNoTextFontsOrExternalReferences()
    {
        var files = Directory.GetFiles(Path.Combine(Repository(), "src", "SpanDraft.Desktop", "Assets", "Icons", "Toolbar"), "*.svg");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var svg = XDocument.Load(file);
            Assert.DoesNotContain(svg.Descendants(), e => e.Name.LocalName is "text" or "tspan" or "textPath" ||
                e.Name.LocalName.StartsWith("font", StringComparison.OrdinalIgnoreCase));
            var content = string.Join("\n", svg.Descendants().Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.ToString())
                .Concat(svg.DescendantNodes().OfType<XText>().Select(t => t.Value)));
            Assert.DoesNotMatch(@"(?i)\bfont\b|(?:https?:)?//", content);
        }

    }

    [Fact]
    public void ProductContainsNativeResourcesWithoutSvgAssembliesOrEmbeddedSources()
    {
        var desktop = typeof(SpanDraft.Desktop.App).Assembly;
        Assert.DoesNotContain(desktop.GetReferencedAssemblies(), a => a.Name!.StartsWith("Svg", StringComparison.OrdinalIgnoreCase));
        var assets = AssetLoader.GetAssets(new Uri("avares://SpanDraft.Desktop/Assets/Icons/"), null).ToArray();
        Assert.DoesNotContain(assets, uri => uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("SpanDraft.ico")]
    [InlineData("app-icon-1024.png")]
    public void EmbeddedAppIconsMatchTheCurrentSourceAssets(string name)
    {
        using var stream = AssetLoader.Open(new Uri("avares://SpanDraft.Desktop/Assets/AppIcon/" + name));
        using var embedded = new MemoryStream();
        stream.CopyTo(embedded);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Repository(), "src", "SpanDraft.Desktop", "Assets", "AppIcon", name)), embedded.ToArray());
    }
}

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace SpanDraft.Tests;

public sealed class MacOSPackagingTests
{
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    [Fact(Skip = "The development app bundle is built on macOS.", SkipUnless = nameof(IsMacOS))]
    public void DevelopmentBundleIncludesTheProjectAndApplicationIcons()
    {
        string desktop = DesktopProjectDirectory();
        string configuration = typeof(MacOSPackagingTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        string contents = Path.Combine(desktop, "bin", configuration, framework, "SpanDraft.app", "Contents");
        var plist = XDocument.Load(Path.Combine(contents, "Info.plist")).Root!.Element("dict")!;

        const string projectTypeIdentifier = "io.github.stephan-entw.spandraft.project";
        var documentType = Assert.Single(Value(plist, "UTExportedTypeDeclarations").Elements("dict"),
            type => Value(type, "UTTypeIdentifier").Value == projectTypeIdentifier);
        var extension = Assert.Single(Value(Value(documentType, "UTTypeTagSpecification"), "public.filename-extension").Elements("string"));
        Assert.Equal("spandraft", extension.Value);
        Assert.Equal("spandraft-project.icns", Value(documentType, "UTTypeIconFile").Value);

        var projectType = Assert.Single(Value(plist, "CFBundleDocumentTypes").Elements("dict"),
            type => Value(type, "LSItemContentTypes").Elements("string").Any(contentType => contentType.Value == projectTypeIdentifier));
        Assert.Equal("Editor", Value(projectType, "CFBundleTypeRole").Value);
        Assert.Equal("spandraft-project.icns", Value(projectType, "CFBundleTypeIconFile").Value);
        Assert.Equal("SpanDraft.icns", Value(plist, "CFBundleIconFile").Value);

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(desktop, "Assets", "ProjectFileIcon", "spandraft-project.icns")),
            File.ReadAllBytes(Path.Combine(contents, "Resources", "spandraft-project.icns")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(desktop, "Assets", "AppIcon", "SpanDraft.icns")),
            File.ReadAllBytes(Path.Combine(contents, "Resources", "SpanDraft.icns")));
    }

    private static XElement Value(XElement dictionary, string key) =>
        Assert.Single(Assert.Single(dictionary.Elements("key"), element => element.Value == key)
            .ElementsAfterSelf().Take(1));

    private static string DesktopProjectDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../src/SpanDraft.Desktop"));
}

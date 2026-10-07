using System.Globalization;
using System.Reflection;
using System.Resources;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Resources;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopApplicationMetadataTests
{
    [Fact]
    public void BundledLicenseMatchesTheCompleteRepositoryFile()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpanDraft.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var expected = File.ReadAllText(Path.Combine(directory.FullName, "LICENSE"));
        Assert.NotEmpty(expected);
        Assert.Equal(expected, ApplicationLicense.ReadText());
    }

    [Fact]
    public void AboutInformationComesFromDesktopAssemblyInsteadOfTestHost()
    {
        var assembly = typeof(App).Assembly;
        Assert.NotSame(Assembly.GetEntryAssembly(), assembly);
        Assert.Equal(assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product, ApplicationMetadata.Product);
        Assert.Equal(assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright, ApplicationMetadata.Copyright);
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal(metadata["LicenseDisplayName"], ApplicationMetadata.License);
        Assert.Equal(metadata["LicenseShortName"], ApplicationMetadata.LicenseShortName);
        Assert.Equal(metadata["RepositoryUrl"], ApplicationMetadata.RepositoryUrl.AbsoluteUri);
        Assert.Equal("https", ApplicationMetadata.RepositoryUrl.Scheme);
        Assert.Equal(ApplicationMetadata.FormatVersion(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version), ApplicationMetadata.Version);
        Assert.DoesNotContain("+", ApplicationMetadata.Version);
        Assert.All(new[] { ApplicationMetadata.Product, ApplicationMetadata.Version, ApplicationMetadata.Copyright,
            ApplicationMetadata.License }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Theory]
    [InlineData("2.4.1+abc123", "2.4.1")]
    [InlineData("2.4.1-rc.2+abc123", "2.4.1-rc.2")]
    [InlineData("2.4.1-rc.2", "2.4.1-rc.2")]
    [InlineData("2.4.1", "2.4.1")]
    [InlineData(null, "3.2.1.0")]
    [InlineData("", "3.2.1.0")]
    [InlineData(" ", "3.2.1.0")]
    public void DisplayVersionPreservesPrereleaseAndFallsBackToAssembly(string? informationalVersion, string expected)
    {
        Assert.Equal(expected, ApplicationMetadata.FormatVersion(informationalVersion, new Version(3, 2, 1, 0)));
    }

    [Theory]
    [InlineData("de-DE", "Über SpanDraft", "Open-Source-Tool zur Balkenberechnung")]
    [InlineData("en-US", "About SpanDraft", "Open-source tool for beam analysis")]
    public void AboutResourcesAreAvailableInBothLanguages(string cultureName, string title, string description)
    {
        var resources = new ResourceManager("SpanDraft.Desktop.Resources.Strings", typeof(Strings).Assembly);
        var culture = CultureInfo.GetCultureInfo(cultureName);
        Assert.Equal(title, resources.GetString(nameof(Strings.About), culture));
        Assert.Equal(description, resources.GetString(nameof(Strings.AboutDescription), culture));
        Assert.Equal("GitHub", resources.GetString(nameof(Strings.GitHubLink), culture));
        Assert.Equal("Version", resources.GetString(nameof(Strings.VersionLabel), culture));
        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(nameof(Strings.LinkOpenError), culture)));
        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(nameof(Strings.LicenseOpenError), culture)));
    }
}

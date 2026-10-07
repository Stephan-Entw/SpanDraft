using System.Reflection;

namespace SpanDraft.Desktop.Resources;

/// <summary>Product information from the desktop build, also when hosted by a test runner.</summary>
public static class ApplicationMetadata
{
    private static readonly Assembly Assembly = typeof(ApplicationMetadata).Assembly;

    public static string Product { get; } = Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
    public static string Version { get; } = FormatVersion(
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Assembly.GetName().Version);
    public static string Copyright { get; } = Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;
    public static string License { get; } = Metadata("LicenseDisplayName");
    public static string LicenseShortName { get; } = Metadata("LicenseShortName");
    public static Uri RepositoryUrl { get; } = new(Metadata("RepositoryUrl"), UriKind.Absolute);

    public static string FormatVersion(string? informationalVersion, Version? assemblyVersion)
    {
        var version = informationalVersion?.Split('+', 2)[0];
        return !string.IsNullOrWhiteSpace(version) ? version : assemblyVersion?.ToString() ?? string.Empty;
    }

    private static string Metadata(string key) => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value!;
}

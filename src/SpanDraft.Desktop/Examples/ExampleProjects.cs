using System.Globalization;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.Examples;

/// <summary>A shipped example: a language independent id and its localized name (window title, quit question).</summary>
internal sealed record ExampleProject(string Id, Func<string> LocalizedName)
{
    public string Name => LocalizedName();
}

internal enum ExampleRouteStatus { Example, InvalidLink, UnknownExample }

internal readonly record struct ExampleRoute(ExampleRouteStatus Status, ExampleProject? Example = null);

/// <summary>
/// The explicit list of known examples and the one route that opens them: <c>spandraft://beispiel/&lt;id&gt;</c>. The route is
/// checked on the original string, never on a normalized <see cref="Uri"/>, so dot segments, escapes or a trailing slash
/// cannot turn into a valid route. Nothing in a link is ever resolved as a path, URL or command.
/// </summary>
internal static class ExampleCatalog
{
    private const string RoutePrefix = LaunchRequest.UriScheme + "://beispiel/";

    public static IReadOnlyList<ExampleProject> All { get; } =
    [
        new("einfeldtraeger", static () => Strings.ExampleSimplySupportedBeam),
        new("kragarm", static () => Strings.ExampleCantilever),
        new("abgesetzte-welle", static () => Strings.ExampleSteppedShaft),
    ];

    public static ExampleRoute Parse(string? uri)
    {
        if (uri is null || !uri.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase))
            return new(ExampleRouteStatus.InvalidLink);
        string id = uri[RoutePrefix.Length..];
        if (All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal)) is { } example)
            return new(ExampleRouteStatus.Example, example);
        // A plain single segment names an example this version does not know; anything else is no valid link at all.
        return new(id.Length > 0 && id.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? ExampleRouteStatus.UnknownExample : ExampleRouteStatus.InvalidLink);
    }
}

/// <summary>Supplies the project bytes of a known example; null when the example is not available.</summary>
public interface IExampleProjectSource
{
    Task<byte[]?> ReadAsync(string id);
}

/// <summary>
/// The shipped examples as embedded resources <c>SpanDraft.Desktop.Examples.&lt;id&gt;.&lt;language&gt;.spandraft</c>, falling
/// back to English. Without a shipped resource the example is reported as not available.
/// </summary>
internal sealed class EmbeddedExampleProjectSource : IExampleProjectSource
{
    public async Task<byte[]?> ReadAsync(string id)
    {
        string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
        var assembly = typeof(EmbeddedExampleProjectSource).Assembly;
        using var stream = assembly.GetManifestResourceStream($"SpanDraft.Desktop.Examples.{id}.{language}.spandraft")
            ?? assembly.GetManifestResourceStream($"SpanDraft.Desktop.Examples.{id}.en.spandraft");
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}

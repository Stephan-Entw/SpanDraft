namespace SpanDraft.Desktop.Activation;

public enum LaunchRequestType { Activate, OpenProject, OpenUri }

/// <summary>The single internal model every activation source produces; small, validated and bounded.</summary>
public sealed record LaunchRequest(LaunchRequestType Type, string? Value = null)
{
    public const int MaxValueLength = 4096;
    public const string UriScheme = "spandraft";
    public const string ProjectExtension = ".spandraft";

    public static LaunchRequest Activate { get; } = new(LaunchRequestType.Activate);
    public static LaunchRequest OpenProject(string absolutePath) => new(LaunchRequestType.OpenProject, absolutePath);
    public static LaunchRequest OpenUri(string uri) => new(LaunchRequestType.OpenUri, uri);

    public bool IsValid => Type switch
    {
        LaunchRequestType.Activate => Value is null,
        LaunchRequestType.OpenProject => Value is { Length: > 0 and <= MaxValueLength } path
            && !path.Contains('\0') && Path.IsPathFullyQualified(path),
        LaunchRequestType.OpenUri => Value is { Length: > 0 and <= MaxValueLength } uri
            && Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.Scheme, UriScheme, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>Startup command line: one project path or one spandraft:// URI, anything else is a plain activation.</summary>
    public static LaunchRequest FromArguments(string[]? args)
    {
        if (args is not [{ Length: > 0 } single]) return Activate;
        if (Uri.TryCreate(single, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, UriScheme, StringComparison.OrdinalIgnoreCase))
            return OpenUri(single);
        if (string.Equals(Path.GetExtension(single), ProjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            try { return OpenProject(Path.GetFullPath(single)); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return Activate;
    }
}

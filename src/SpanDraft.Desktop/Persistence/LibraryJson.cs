using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpanDraft.Desktop.Persistence;

public sealed class LibraryFormatException(string message, Exception? inner = null) : Exception(message, inner);

internal static class LibraryJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false
    };

    internal static TResult Read<TFile, TResult>(ReadOnlySpan<byte> bytes, string expectedFormat, int expectedVersion,
        Func<TFile, TResult> map) where TFile : class
    {
        try
        {
            var root = JsonElement.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.String || format.GetString() != expectedFormat)
                throw new LibraryFormatException("Not a " + expectedFormat + " file.");
            if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != expectedVersion)
                throw new LibraryFormatException("Missing or unsupported library format version.");
            return map(Required(root.Deserialize<TFile>(Options), "library"));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
        {
            throw new LibraryFormatException("Invalid library data: " + e.Message, e);
        }
    }

    internal static T Required<T>(T? value, string field) where T : class => value
        ?? throw new LibraryFormatException("Missing or null field: " + field);

    internal static double Number(double value, string field) => double.IsFinite(value) ? value
        : throw new LibraryFormatException("Non-finite number: " + field);
}

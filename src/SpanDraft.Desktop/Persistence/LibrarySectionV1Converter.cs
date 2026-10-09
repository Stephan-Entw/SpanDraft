using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpanDraft.Desktop.Persistence;

/// <summary>Stable wire discrimination, independent of property order and CLR type names.</summary>
internal sealed class LibrarySectionV1Converter : JsonConverter<LibrarySectionV1>
{
    public override LibrarySectionV1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing or invalid section type.");
        return type.GetString() switch
        {
            "rectangle" => root.Deserialize<RectangleSectionLibraryV1>(options)!,
            "rectangularHollow" => root.Deserialize<RectangularHollowSectionLibraryV1>(options)!,
            "circle" => root.Deserialize<CircleSectionLibraryV1>(options)!,
            "circularHollow" => root.Deserialize<CircularHollowSectionLibraryV1>(options)!,
            "iSection" => root.Deserialize<ISectionLibraryV1>(options)!,
            "uSection" => root.Deserialize<USectionLibraryV1>(options)!,
            "tSection" => root.Deserialize<TSectionLibraryV1>(options)!,
            "angle" => root.Deserialize<AngleSectionLibraryV1>(options)!,
            "manual" => root.Deserialize<ManualSectionLibraryV1>(options)!,
            _ => throw new JsonException("Unknown section type.")
        };
    }

    public override void Write(Utf8JsonWriter writer, LibrarySectionV1 value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case RectangleSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case RectangularHollowSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case CircleSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case CircularHollowSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case ISectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case USectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case TSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case AngleSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            case ManualSectionLibraryV1 s: JsonSerializer.Serialize(writer, s, options); break;
            default: throw new LibraryFormatException("Unknown section type.");
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpanDraft.Desktop.Persistence;

/// <summary>Stable wire discrimination, independent of property order and CLR type names.</summary>
public sealed class SectionV2Converter : JsonConverter<SectionV2>
{
    public override SectionV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing or invalid section type.");
        return type.GetString() switch
        {
            "rectangle" => root.Deserialize<RectangleSectionV2>(options)!,
            "rectangularHollow" => root.Deserialize<RectangularHollowSectionV2>(options)!,
            "circle" => root.Deserialize<CircleSectionV2>(options)!,
            "circularHollow" => root.Deserialize<CircularHollowSectionV2>(options)!,
            "iSection" => root.Deserialize<ISectionV2>(options)!,
            "uSection" => root.Deserialize<USectionV2>(options)!,
            "tSection" => root.Deserialize<TSectionV2>(options)!,
            "angle" => root.Deserialize<AngleSectionV2>(options)!,
            "manual" => root.Deserialize<ManualSectionV2>(options)!,
            _ => throw new JsonException("Unknown section type.")
        };
    }

    public override void Write(Utf8JsonWriter writer, SectionV2 value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case RectangleSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case RectangularHollowSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case CircleSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case CircularHollowSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case ISectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case USectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case TSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case AngleSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            case ManualSectionV2 s: JsonSerializer.Serialize(writer, s, options); break;
            default: throw new ProjectFormatException("Unknown section type.");
        }
    }
}

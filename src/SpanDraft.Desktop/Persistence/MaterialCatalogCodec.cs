using SpanDraft.Desktop.Libraries;
using static SpanDraft.Desktop.Persistence.LibraryJson;

namespace SpanDraft.Desktop.Persistence;

public static class MaterialCatalogCodec
{
    public const string Format = "SpanDraft.MaterialCatalog";
    public const int Version = 1;
    private const string ResourceName = "SpanDraft.Desktop.Libraries.material-catalog.json";

    public static BuiltInMaterialCatalog Deserialize(ReadOnlySpan<byte> bytes) =>
        Read<MaterialCatalogV1, BuiltInMaterialCatalog>(bytes, Format, Version,
            file => new(Required(file.Entries, "entries").Select(MaterialLibraryMapping.FromDto)));

    public static BuiltInMaterialCatalog LoadBuiltIn()
    {
        using var resource = typeof(MaterialCatalogCodec).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new LibraryFormatException("Missing built-in material catalog resource.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return Deserialize(buffer.ToArray());
    }
}

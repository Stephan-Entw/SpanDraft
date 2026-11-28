using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;

/// <summary>
/// The bundled example projects (DEV-64). One mechanical definition per example feeds both its help figures and its
/// project files <c>&lt;language&gt;/examples/&lt;id&gt;.spandraft</c>; the languages differ only in the project information.
/// Every serialized entity has a fixed identity, so unchanged definitions give byte-identical files.
/// </summary>
public static class HelpExamples
{
    public static IReadOnlyList<string> Languages { get; } = ["de", "en"];

    public sealed record Example(string Id, Func<EditorDocument> Document, string TitleDe, string TitleEn, string ArticleDe, string ArticleEn);

    public static IReadOnlyList<Example> All { get; } =
    [
        new("einfeldtraeger", SimplySupportedBeam, "Balken mit mittiger Einzelkraft", "Beam with centered point load",
            "Erste Berechnung", "First calculation"),
        new("kragarm", Cantilever, "Kragarm mit Streckenlast und Eigengewicht", "Cantilever with distributed load and self-weight",
            "Kräfte, Momente und Streckenlasten", "Forces, moments and distributed loads"),
        new("abgesetzte-welle", SteppedShaft, "Abgesetzte Welle auf zwei Lagern", "Stepped shaft on two bearings",
            "Balken, Werkstoff und Querschnitt", "Beam, material and cross-section"),
    ];

    // Fixed identities: example number and entity number in an otherwise constant GUID.
    private static Guid Id(int example, int entity) => new($"5d0a{example:x4}-0000-4000-8000-{entity:x12}");

    private static SpanDraft.Core.Materials.Material Material(string name) =>
        MaterialCatalogCodec.LoadBuiltIn().Find(name) ?? throw new InvalidOperationException($"Missing built-in material {name}.");

    private static EditorSegment Segment(int example, int entity, string material, ISectionDefinition section, double length) =>
        new(Material(material), section, SectionAxisDesignation.Y) { Id = Id(example, entity), Length = Length.FromMeters(length) };

    /// <summary>L = 2 m, S235JR, rectangle 40 × 80 mm (y-y), pinned A and roller B at the ends, F1 = −1 kN at L/2.</summary>
    public static EditorDocument SimplySupportedBeam() => new(
        [Segment(1, 1, "S235JR", new RectangleSectionGeometry(Length.FromMillimeters(40), Length.FromMillimeters(80)), 2)],
        supports: [new(Id(1, 2), Length.FromMeters(0), SupportType.Pinned, "A"), new(Id(1, 3), Length.FromMeters(2), SupportType.Roller, "B")],
        loads: [new EditorPointForce(Id(1, 4), Length.FromMeters(1), Force.FromNewtons(-1000), "F1")],
        namingState: new NamingState(nextSupportOrdinal: 3, nextForceNumber: 2));

    /// <summary>L = 1.5 m, S235JR, rectangle 40 × 80 mm (y-y), fixed support A at x = 0, q1 = −2 kN/m over the whole length, self-weight.</summary>
    public static EditorDocument Cantilever() => new(
        [Segment(2, 1, "S235JR", new RectangleSectionGeometry(Length.FromMillimeters(40), Length.FromMillimeters(80)), 1.5)],
        supports: [new(Id(2, 2), Length.FromMeters(0), SupportType.Fixed, "A")],
        distributedLoads: [new(Id(2, 3), Length.FromMeters(0), Length.FromMeters(1.5), ForcePerLength.FromNewtonsPerMeter(-2000), "q1")],
        namingState: new NamingState(nextSupportOrdinal: 2, nextDistributedLoadNumber: 2),
        selfWeight: new EditorSelfWeight(Id(2, 4), Enabled: true));

    /// <summary>
    /// 42CrMo4 +QT, Ø30 (0–0.1 m), Ø40 (0.1–0.3 m), Ø30 (0.3–0.4 m), pinned A at x = 0, roller B at x = 0.4 m,
    /// F1 = −5 kN at x = 0.15 m, no self-weight.
    /// </summary>
    public static EditorDocument SteppedShaft() => new(
        [
            Segment(3, 1, "42CrMo4 +QT", new CircleSectionGeometry(Length.FromMillimeters(30)), 0.1),
            Segment(3, 2, "42CrMo4 +QT", new CircleSectionGeometry(Length.FromMillimeters(40)), 0.2),
            Segment(3, 3, "42CrMo4 +QT", new CircleSectionGeometry(Length.FromMillimeters(30)), 0.1),
        ],
        supports: [new(Id(3, 4), Length.FromMeters(0), SupportType.Pinned, "A"), new(Id(3, 5), Length.FromMeters(0.4), SupportType.Roller, "B")],
        loads: [new EditorPointForce(Id(3, 6), Length.FromMeters(0.15), Force.FromNewtons(-5000), "F1")],
        namingState: new NamingState(nextSupportOrdinal: 3, nextForceNumber: 2));

    public static ProjectState State(Example example, string language) => new(example.Document(), new EditorPresentationState(),
        language == "de"
            ? new ProjectInfo(example.TitleDe, "SpanDraft-Beispiele", "Hilfe: " + example.ArticleDe, null)
            : new ProjectInfo(example.TitleEn, "SpanDraft examples", "Help: " + example.ArticleEn, null));

    public static string RelativePath(Example example, string language) => Path.Combine(language, "examples", example.Id + ".spandraft");

    public static byte[] Serialize(Example example, string language) => ProjectFileCodec.Serialize(State(example, language));

    public static void Generate(string assetRoot)
    {
        foreach (string language in Languages)
            foreach (var example in All)
            {
                string path = Path.Combine(assetRoot, RelativePath(example, language));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, Serialize(example, language));
            }
    }

    /// <summary>
    /// What is wrong with the committed example files: missing, unreadable, not matching the definition (stale or changed
    /// by hand) or unexpected. Reads only; an empty list means the files are current.
    /// </summary>
    public static IReadOnlyList<string> Check(string assetRoot)
    {
        var problems = new List<string>();
        foreach (string language in Languages)
        {
            string directory = Path.Combine(assetRoot, language, "examples");
            var expected = All.Select(e => e.Id + ".spandraft").ToHashSet(StringComparer.Ordinal);
            if (Directory.Exists(directory))
                foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
                    if (!expected.Contains(Path.GetFileName(file)))
                        problems.Add($"Unexpected example file: {Path.Combine(language, "examples", Path.GetFileName(file))}");
            foreach (var example in All)
            {
                string relative = RelativePath(example, language);
                string path = Path.Combine(assetRoot, relative);
                if (!File.Exists(path)) { problems.Add($"Missing example file: {relative}"); continue; }
                byte[] actual = File.ReadAllBytes(path);
                try { ProjectFileCodec.Deserialize(actual); }
                catch (ProjectFormatException error)
                {
                    problems.Add($"Damaged example file: {relative}: {error.Message}");
                    continue;
                }
                if (!actual.AsSpan().SequenceEqual(Serialize(example, language)))
                    problems.Add($"Example file does not match its definition (regenerate the help assets): {relative}");
            }
        }
        return problems;
    }
}

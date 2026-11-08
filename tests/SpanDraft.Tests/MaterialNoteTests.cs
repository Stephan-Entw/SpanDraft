using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class MaterialNoteTests
{

    [Fact]
    public void NormalizesCopiesAndComparesTheCompleteNote()
    {
        var source = new Dictionary<string, string> { [" FR "] = " Français ", [" DE "] = " Deutsch " };
        var note = new MaterialNote(" Default ", source);
        source[" DE "] = "changed";
        Assert.Equal("Default", note.DefaultText);
        Assert.Equal(new[] { "de", "fr" }, note.Translations.Keys);
        Assert.Equal("Deutsch", note.Translations["de"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)note.Translations).Add("es", "Texto"));
        var equivalent = new MaterialNote("Default", new Dictionary<string, string> { ["de"] = "Deutsch", ["fr"] = "Français" });
        Assert.Equal(note, equivalent); Assert.Equal(note.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(note, new MaterialNote("Default", new Dictionary<string, string> { ["de"] = "Deutsch" }));
        Assert.NotEqual(note, new MaterialNote("Other", source));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \t ")]
    public void RejectsEmptyDefaultKeysAndTranslations(string? empty)
    {
        Assert.ThrowsAny<ArgumentException>(() => new MaterialNote(empty!));
        Assert.ThrowsAny<ArgumentException>(() => new MaterialNote("Default", [new(empty!, "Text")]));
        Assert.ThrowsAny<ArgumentException>(() => new MaterialNote("Default", [new("de", empty!)]));
    }

    [Fact]
    public void RejectsAmbiguousLanguageKeys() => Assert.Throws<ArgumentException>(() =>
        new MaterialNote("Default", [new("de-DE", "First"), new(" DE-de ", "Second")]));

    [Theory]
    [InlineData("de-DE", "Exact")] [InlineData("de-AT", "Parent")]
    [InlineData("zh-Hant-TW", "Chinese")] [InlineData("en-US", "Default")] [InlineData("", "Default")]
    public void ResolvesExplicitCultureThroughAllParents(string culture, string expected)
    {
        var note = new MaterialNote("Default", [new("DE-de", "Exact"), new("de", "Parent"), new("zh", "Chinese")]);
        Assert.Equal(expected, MaterialNoteDisplay.Resolve(note, CultureInfo.GetCultureInfo(culture)));
        Assert.Null(MaterialNoteDisplay.Resolve(null, CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("S235JR", "Reference value for t ≤ 16 mm; lower at greater thickness.", "Referenzwert für t ≤ 16 mm; bei größerer Dicke geringer.")]
    [InlineData("S355JR", "Reference value for t ≤ 16 mm; lower at greater thickness.", "Referenzwert für t ≤ 16 mm; bei größerer Dicke geringer.")]
    [InlineData("X5CrNi18-10", "Reference value for long products at 20 °C.", "Referenzwert für Langprodukte bei 20 °C.")]
    [InlineData("X2CrNiMo17-12-2", "Reference value for long products at 20 °C.", "Referenzwert für Langprodukte bei 20 °C.")]
    [InlineData("42CrMo4 +QT", "Reference value for +QT round bar, Ø 40–100 mm.", "Referenzwert für +QT-Rundstahl, Ø 40–100 mm.")]
    [InlineData("34CrNiMo6 +QT", "Reference value for +QT round bar, Ø 40–100 mm.", "Referenzwert für +QT-Rundstahl, Ø 40–100 mm.")]
    [InlineData("AlMgSi0,5 T66", "Reference value for T66 profiles up to 25 mm wall thickness.", "Referenzwert für T66-Profile bis 25 mm Wanddicke.")]
    [InlineData("AlMg3 H22", "Reference value for H22 sheet and plate up to 40 mm thickness.", "Referenzwert für H22-Blech und -Platte bis 40 mm Dicke.")]
    public void BuiltInNotesMatchTheCuratedTexts(string name, string english, string german)
    {
        var note = MaterialCatalogCodec.LoadBuiltIn().Find(name)!.Note!;
        Assert.Equal(english, note.DefaultText); Assert.Single(note.Translations);
        Assert.Equal(english, MaterialNoteDisplay.Resolve(note, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(german, MaterialNoteDisplay.Resolve(note, CultureInfo.GetCultureInfo("de-DE")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FullSnapshotRoundTripsAcrossIndependentWireContractsAndRecoveries(bool translated)
    {
        var note = new MaterialNote("Default\nSecond line", translated ? [new("fr", "Français"), new("de", "Deutsch")] : null);
        var state = State(); state = state with { Document = state.Document with { Material = MaterialNoteTestSupport.WithNote(state.Document.Material, note) } };
        var libraryBytes = MaterialLibraryCodec.Serialize(new([state.Document.Material]));
        Assert.Equal(note, Assert.Single(MaterialLibraryCodec.Deserialize(libraryBytes).All).Note);
        Assert.Equal(libraryBytes, MaterialLibraryCodec.Serialize(MaterialLibraryCodec.Deserialize(libraryBytes)));
        var projectBytes = ProjectFileCodec.Serialize(state);
        Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(projectBytes)));
        Assert.Equal(projectBytes, ProjectFileCodec.Serialize(ProjectFileCodec.Deserialize(projectBytes)));
        Assert.Equal(4, JsonNode.Parse(projectBytes)!["formatVersion"]!.GetValue<int>());
        Assert.Equal(1, JsonNode.Parse(libraryBytes)!["formatVersion"]!.GetValue<int>());
        if (!translated) Assert.DoesNotContain("translations", Encoding.UTF8.GetString(projectBytes));

        var files = new Files();
        files.Data["project"] = ProjectRecovery.Encode(new(state, null, Now));
        var recovered = await new ProjectRecovery(files, "project").ReadAsync();
        Assert.True(state.ContentEquals(recovered!.State));
        var draft = new ProjectSetupDraft(Material: state.Document.Material);
        files.Data["setup"] = ProjectSetupRecovery.Encode(new(draft, Now));
        var recoveredDraft = await new ProjectSetupRecovery(files, "setup").ReadAsync();
        Assert.True(draft.ContentEquals(recoveredDraft!.Draft));
    }

    [Fact]
    public void MissingNotesRemainMissingEvenWhenTheNameMatchesTheCatalog()
    {
        var material = MaterialNoteTestSupport.WithNote(MaterialCatalogCodec.LoadBuiltIn().Find("S235JR")!, null);
        var library = MaterialLibraryCodec.Serialize(new([material]));
        Assert.DoesNotContain("\"note\"", Encoding.UTF8.GetString(library));
        Assert.Null(Assert.Single(MaterialLibraryCodec.Deserialize(library).All).Note);
        var state = State(); state = state with { Document = state.Document with { Material = material } };
        var bytes = ProjectFileCodec.Serialize(state);
        Assert.DoesNotContain("\"note\"", Encoding.UTF8.GetString(bytes));
        Assert.Null(ProjectFileCodec.Deserialize(bytes).Document.Material.Note);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"default\":null}")] [InlineData("{\"default\":\" \"}")]
    [InlineData("{\"default\":\"Text\",\"translations\":{\"de\":\"\"}}")]
    [InlineData("{\"default\":\"Text\",\"translations\":{\"de\":\"A\",\"DE\":\"B\"}}")]
    public void InvalidNotesAreFormatErrors(string invalid)
    {
        var project = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!;
        project["document"]!["material"]!["note"] = JsonNode.Parse(invalid);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString())));
        var library = LibraryTestSupport.MaterialJson();
        library["entries"]![0]!["material"]!["note"] = JsonNode.Parse(invalid);
        Assert.Throws<LibraryFormatException>(() => MaterialLibraryCodec.Deserialize(Encoding.UTF8.GetBytes(library.ToJsonString())));
    }

    [Fact]
    public void InvisibleTranslationChangesContentAndHistoryButNotMechanics()
    {
        var state = State();
        var first = new MaterialNote("Default", [new("de", "Deutsch"), new("fr", "First")]);
        var second = new MaterialNote("Default", [new("de", "Deutsch"), new("fr", "Second")]);
        state = state with { Document = state.Document with { Material = MaterialNoteTestSupport.WithNote(state.Document.Material, first) } };
        var next = state with { Document = state.Document with { Material = MaterialNoteTestSupport.WithNote(state.Document.Material, second) } };
        Assert.False(MaterialSnapshotComparison.Equals(state.Document.Material, next.Document.Material));
        Assert.False(state.ContentEquals(next));
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(state.Document.ToBeamModel(), next.Document.ToBeamModel()));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(state, next));
        var session = ProjectSession.Open(state, TestPath("note.spandraft"));
        Assert.True(session.Commit(next)); Assert.True(session.IsDirty);
        Assert.True(session.Undo()); Assert.False(session.IsDirty); Assert.Equal(first, session.CurrentRevision.State.Document.Material.Note);
        Assert.True(session.Redo()); Assert.Equal(second, session.CurrentRevision.State.Document.Material.Note);
    }
}

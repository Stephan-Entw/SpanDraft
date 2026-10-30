using System.Text.Json;
using System.Text.Json.Nodes;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectSelfWeightTests
{
    [Theory]
    [InlineData(true, true)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(false, false)]
    public void V3RoundTripRetainsWeightIdentityVisibilityAndAnnotation(bool enabled, bool visible)
    {
        var d = DesktopSelfWeightTests.Document(true);
        d = d with { SelfWeight = d.SelfWeight! with { Enabled = enabled } };
        var state = new ProjectState(d, new([new(d.SelfWeight!.Id, new(31.25, -17.75))], visible));
        var bytes = ProjectFileCodec.Serialize(state);
        var json = JsonNode.Parse(bytes)!;
        Assert.Equal(3, json["formatVersion"]!.GetValue<int>());
        var weight = json["document"]!["selfWeight"]!.AsObject();
        Assert.Equal(new[] { "id", "enabled" }, weight.Select(e => e.Key));
        Assert.Equal(enabled, weight["enabled"]!.GetValue<bool>());
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(state.ContentEquals(loaded));
        Assert.Equal(state.Document.SelfWeight, loaded.Document.SelfWeight);
        Assert.Equal(visible, loaded.Presentation.SelfWeightVisible);
        Assert.Equal(state.Presentation.AnnotationOffsets, loaded.Presentation.AnnotationOffsets);
        var retained = loaded.Presentation.RetainEntities(loaded.Document.WithLoads([]));
        Assert.Same(loaded.Presentation, retained);
        Assert.Equal(visible, loaded.Presentation.WithOffset(d.Supports[0].Id, new(1, 2)).SelfWeightVisible);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void HistoricalDocumentsWithoutWeightPreserveTheirMechanicalResults(int version)
    {
        var state = new ProjectState(DesktopSelfWeightTests.Document(), new());
        var json = JsonNode.Parse(ProjectFileCodec.Serialize(state))!.AsObject();
        json["formatVersion"] = version;
        json["document"]!.AsObject().Remove("selfWeight");
        json["presentation"]!.AsObject().Remove("selfWeightVisible");
        var loaded = ProjectFileCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(json));
        Assert.Null(loaded.Document.SelfWeight); Assert.True(loaded.Presentation.SelfWeightVisible);
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(state.Document.ToBeamModel(), loaded.Document.ToBeamModel()));
        SolverTestSupport.SameResults(SolverTestSupport.Solve(state.Document.ToBeamModel()), SolverTestSupport.Solve(loaded.Document.ToBeamModel()));
    }

    [Theory]
    [InlineData("density")] [InlineData("emptyId")] [InlineData("duplicateId")]
    [InlineData("missingEnabled")] [InlineData("missingId")] [InlineData("conflict")]
    [InlineData("orphanOffset")] [InlineData("duplicateOffset")]
    public void MalformedWeightDataIsRejected(string field)
    {
        var d = DesktopSelfWeightTests.Document(true);
        var state = new ProjectState(d, new([new(d.SelfWeight!.Id, new(1, 2))]));
        var json = JsonNode.Parse(ProjectFileCodec.Serialize(state))!;
        var document = json["document"]!; var weight = document["selfWeight"]!.AsObject();
        switch (field)
        {
            case "density": document["material"]!.AsObject().Remove("density"); break;
            case "emptyId": weight["id"] = Guid.Empty; break;
            case "duplicateId": weight["id"] = d.Supports[0].Id; break;
            case "missingEnabled": weight.Remove("enabled"); break;
            case "missingId": weight.Remove("id"); break;
            case "conflict": document["supports"]![0]!["name"] = "G"; break;
            case "orphanOffset": json["presentation"]!["annotationOffsets"]![0]!["entityId"] = Guid.NewGuid(); break;
            case "duplicateOffset": json["presentation"]!["annotationOffsets"]!.AsArray().Add(json["presentation"]!["annotationOffsets"]![0]!.DeepClone()); break;
        }
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(json)));
    }

    [Fact]
    public void LegacyReservedNamesRoundTripWithoutWeightAndDisabledWeightDoesNotRequireDensity()
    {
        var d = DesktopSelfWeightTests.Document();
        d = d.WithSupports([d.Supports[0] with { Name = "G" }]) with { Material = DesktopSelfWeightTests.Steel(null),
            SelfWeight = new(Guid.NewGuid(), false) };
        var state = new ProjectState(d, new([new(d.SelfWeight.Id, new(5, 6))], false));
        Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(state))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RecoveryRetainsDisabledAndActiveWeightIncludingPresentation(bool enabled)
    {
        var app = new ProjectTestSupport.App();
        var d = DesktopSelfWeightTests.Document(true);
        d = d with { SelfWeight = d.SelfWeight! with { Enabled = enabled } };
        var state = new ProjectState(d, new([new(d.SelfWeight!.Id, new(-24, 30))], false));
        Assert.True(app.Main.Session!.Commit(state));
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        var recovered = Assert.IsType<RecoverySnapshot>(await app.Recovery.ReadAsync());
        Assert.True(state.ContentEquals(recovered.State));
        Assert.Equal(3, JsonNode.Parse(app.Files.Data[ProjectTestSupport.App.Slot])!["project"]!["formatVersion"]!.GetValue<int>());
        Assert.True(ProjectSession.Restore(recovered.State, recovered.OriginalFilePath).IsDirty);
    }
}

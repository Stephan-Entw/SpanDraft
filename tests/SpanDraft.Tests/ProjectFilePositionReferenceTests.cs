using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Format V5 stores a position either as a distance from the beam start or as a reference to a segment, never as
/// both, and stores the segments by length and identity.
/// </summary>
public sealed class ProjectFilePositionReferenceTests
{
    private static readonly Guid S1 = Guid.Parse("5e5e5e5e-0000-4000-8000-000000000001");
    private static readonly Guid S2 = Guid.Parse("5e5e5e5e-0000-4000-8000-000000000002");
    private static readonly Guid S3 = Guid.Parse("5e5e5e5e-0000-4000-8000-000000000003");

    private static byte[] Fixture() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "V5", "segment-references.spandraft"));

    private static JsonNode Root() => JsonNode.Parse(Fixture())!;
    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
    private static ProjectState Load(JsonNode node) => ProjectFileCodec.Deserialize(Bytes(node));

    [Fact]
    public void TheFixtureLoadsTheChainAndResolvesEveryReference()
    {
        var document = ProjectFileCodec.Deserialize(Fixture()).Document;

        Assert.Equal([S1, S2, S3], document.Segments.Select(s => s.Id));
        Assert.Equal([M(1.2), M(0.8), M(1)], document.Segments.Select(s => s.Length));
        Assert.Equal(M(3), document.Length);
        Assert.Equal([M(0), M(1.2), M(3)], document.Supports.Select(s => s.Position));
        Assert.Equal([null, new PositionRef.AtSegmentEnd(S1), new PositionRef.AtSegmentEnd(S3)],
            document.Supports.Select(s => s.Reference));
        Assert.Equal(M(1.6), document.Loads[0].Position);
        Assert.Equal(new PositionRef.FromSegmentStart(S2, M(0.4)), document.Loads[0].Reference);
        Assert.Equal(M(0.5), document.DistributedLoads[0].StartPosition);
        Assert.Equal(M(2.8), document.DistributedLoads[0].EndPosition);
        Assert.Null(document.DistributedLoads[0].EndReference);
        Assert.Equal(M(2), document.MomentReleases[0].Position);
        Assert.Equal(new PositionRef.AtSegmentEnd(S2), document.MomentReleases[0].Reference);
    }

    [Fact]
    public void RoundtripKeepsTheFileBitForBitAndWritesEachPositionInExactlyOneForm()
    {
        byte[] bytes = Fixture();
        var written = ProjectFileCodec.Serialize(ProjectFileCodec.Deserialize(bytes));
        var root = JsonNode.Parse(written)!["document"]!;

        var supports = root["supports"]!.AsArray();
        Assert.True(supports[0]!.AsObject().ContainsKey("position"));
        Assert.False(supports[0]!.AsObject().ContainsKey("positionRef"));
        Assert.False(supports[1]!.AsObject().ContainsKey("position"));
        Assert.True(supports[1]!["positionRef"]!["atEnd"]!.GetValue<bool>());
        Assert.False(supports[1]!["positionRef"]!.AsObject().ContainsKey("offset"));
        var line = root["distributedLoads"]![0]!.AsObject();
        Assert.True(line.ContainsKey("startPositionRef") && !line.ContainsKey("startPosition"));
        Assert.True(line.ContainsKey("endPosition") && !line.ContainsKey("endPositionRef"));
        Assert.Equal(1.2, root["length"]!.GetValue<double>());
        Assert.Equal(S1, root["segmentId"]!.GetValue<Guid>());
        Assert.Equal(ProjectFileCodec.Serialize(ProjectFileCodec.Deserialize(written)), written);
    }

    [Fact]
    public void ADocumentWithReferencesRoundtripsAndKeepsItsContent()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture());

        var again = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(loaded));

        Assert.True(loaded.ContentEquals(again));
        Assert.Equal(loaded.Document.Supports.Select(s => s.Reference), again.Document.Supports.Select(s => s.Reference));
    }

    [Fact]
    public async Task RecoveryKeepsTheReferences()
    {
        var recovery = new ProjectRecovery(new ProjectTestSupport.Files(), "project-recovery");
        var state = ProjectFileCodec.Deserialize(Fixture());
        await recovery.FlushAsync(state, null);
        var read = (await recovery.ReadAsync())!.State;
        Assert.True(state.ContentEquals(read));
        Assert.Equal(state.Document.MomentReleases[0].Reference, read.Document.MomentReleases[0].Reference);
    }

    [Fact]
    public void AGeometryChangeAfterLoadingMovesWhatRefersToLaterSegmentsAndSavesThemUnchanged()
    {
        var state = ProjectFileCodec.Deserialize(Fixture());

        var change = state.Document.ResizeSegment(S1, M(2));
        Assert.True(change.IsAccepted);
        var again = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(state with { Document = change.Document! }));

        Assert.Equal(M(2), again.Document.Supports[1].Position);
        Assert.Equal(M(2.4), again.Document.Loads[0].Position);
        Assert.Equal(M(2.8), again.Document.DistributedLoads[0].EndPosition);
        Assert.Equal(M(3.8), again.Document.Length);
        Assert.Equal(new PositionRef.FromSegmentStart(S2, M(0.4)), again.Document.Loads[0].Reference);
    }

    [Fact]
    public void ADraftFileWithStartsIsReadAsLengthsWithGeneratedIdentitiesAndWrittenInTheNewForm()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "V5", "segments.spandraft"));

        var state = ProjectFileCodec.Deserialize(bytes);
        var document = state.Document;

        Assert.Equal([M(1.2), M(2)], document.SegmentBoundaries);
        Assert.Equal(3, document.Segments.Select(s => s.Id).Distinct().Count());
        Assert.All(document.Segments, s => Assert.NotEqual(Guid.Empty, s.Id));
        Assert.All(document.Supports, s => Assert.Null(s.Reference));
        var written = JsonNode.Parse(ProjectFileCodec.Serialize(state))!["document"]!;
        Assert.All(written["furtherSegments"]!.AsArray(), segment => Assert.False(segment!.AsObject().ContainsKey("start")));
    }

    [Fact]
    public void APositionGivenInBothFormsOrInNeitherIsRejected()
    {
        var both = Root();
        both["document"]!["supports"]![1]!["position"] = 1.2;
        Assert.Throws<ProjectFormatException>(() => Load(both));

        var neither = Root();
        neither["document"]!["supports"]![1]!.AsObject().Remove("positionRef");
        Assert.Throws<ProjectFormatException>(() => Load(neither));

        var lineBoth = Root();
        lineBoth["document"]!["distributedLoads"]![0]!["startPosition"] = 0.5;
        Assert.Throws<ProjectFormatException>(() => Load(lineBoth));

        var lineNeither = Root();
        lineNeither["document"]!["distributedLoads"]![0]!.AsObject().Remove("endPosition");
        Assert.Throws<ProjectFormatException>(() => Load(lineNeither));
    }

    [Fact]
    public void AReferenceNeedsExactlyOneOfOffsetAndEndAndAKnownSegment()
    {
        var both = Root();
        both["document"]!["supports"]![1]!["positionRef"]!["offset"] = 0.1;
        Assert.Throws<ProjectFormatException>(() => Load(both));

        var neither = Root();
        neither["document"]!["supports"]![1]!["positionRef"]!.AsObject().Remove("atEnd");
        Assert.Throws<ProjectFormatException>(() => Load(neither));

        var endFalse = Root();
        endFalse["document"]!["supports"]![1]!["positionRef"]!["atEnd"] = false;
        Assert.Throws<ProjectFormatException>(() => Load(endFalse));

        var unknown = Root();
        unknown["document"]!["supports"]![1]!["positionRef"]!["segment"] = Guid.NewGuid().ToString();
        Assert.Throws<ProjectFormatException>(() => Load(unknown));

        var empty = Root();
        empty["document"]!["supports"]![1]!["positionRef"]!["segment"] = Guid.Empty.ToString();
        Assert.Throws<ProjectFormatException>(() => Load(empty));

        var missing = Root();
        missing["document"]!["supports"]![1]!["positionRef"]!.AsObject().Remove("segment");
        Assert.Throws<ProjectFormatException>(() => Load(missing));
    }

    [Fact]
    public void ADistanceBeyondItsSegmentOrAResolvedPositionOutsideTheBeamIsRejected()
    {
        var beyond = Root();
        beyond["document"]!["pointLoads"]![0]!["positionRef"]!["offset"] = 0.9;
        Assert.Throws<ProjectFormatException>(() => Load(beyond));

        var atEnd = Root();
        atEnd["document"]!["pointLoads"]![0]!["positionRef"]!["offset"] = 0.8;
        Assert.Equal(M(2), Load(atEnd).Document.Loads[0].Position);

        var outside = Root();
        outside["document"]!["supports"]![0]!["position"] = 3.5;
        Assert.Throws<ProjectFormatException>(() => Load(outside));

        // A hinge may not end up at the beam end through its reference either.
        var hinge = Root();
        hinge["document"]!["momentReleases"]![0]!["positionRef"]!["segment"] = S3.ToString();
        Assert.Throws<ProjectFormatException>(() => Load(hinge));
    }

    [Fact]
    public void SegmentIdentitiesAndLengthsAreChecked()
    {
        var duplicate = Root();
        duplicate["document"]!["furtherSegments"]![0]!["id"] = S1.ToString();
        Assert.Throws<ProjectFormatException>(() => Load(duplicate));

        var empty = Root();
        empty["document"]!["furtherSegments"]![0]!["id"] = Guid.Empty.ToString();
        Assert.Throws<ProjectFormatException>(() => Load(empty));

        var zero = Root();
        zero["document"]!["furtherSegments"]![0]!["length"] = 0;
        Assert.Throws<ProjectFormatException>(() => Load(zero));

        var missing = Root();
        missing["document"]!["furtherSegments"]![0]!.AsObject().Remove("length");
        Assert.Throws<ProjectFormatException>(() => Load(missing));

        var mixed = Root();
        mixed["document"]!["furtherSegments"]![0]!["start"] = 1.2;
        Assert.Throws<ProjectFormatException>(() => Load(mixed));
    }

    [Fact]
    public void FilesWithoutSegmentIdentitiesGetThemAndStayReadable()
    {
        var root = Root();
        var document = root["document"]!;
        document.AsObject().Remove("segmentId");
        foreach (var segment in document["furtherSegments"]!.AsArray()) segment!.AsObject().Remove("id");
        // Without identities nothing can refer to a segment, so the positions are plain distances.
        document["supports"]![1]!.AsObject().Remove("positionRef");
        document["supports"]![1]!["position"] = 1.2;
        document["supports"]![2]!.AsObject().Remove("positionRef");
        document["supports"]![2]!["position"] = 3;
        document["pointLoads"]![0]!.AsObject().Remove("positionRef");
        document["pointLoads"]![0]!["position"] = 1.6;
        document["distributedLoads"]![0]!.AsObject().Remove("startPositionRef");
        document["distributedLoads"]![0]!["startPosition"] = 0.5;
        document["momentReleases"]![0]!.AsObject().Remove("positionRef");
        document["momentReleases"]![0]!["position"] = 2;

        var loaded = Load(root).Document;

        Assert.Equal(3, loaded.Segments.Select(s => s.Id).Distinct().Count());
        Assert.All(loaded.Segments, s => Assert.NotEqual(Guid.Empty, s.Id));
        Assert.Equal([M(1.2), M(0.8), M(1)], loaded.Segments.Select(s => s.Length));
    }

    [Fact]
    public void AReferenceToASegmentThatTheFileGivesNoIdentityIsRejected()
    {
        var root = Root();
        root["document"]!["furtherSegments"]![0]!.AsObject().Remove("id");
        Assert.Throws<ProjectFormatException>(() => Load(root));
    }
}

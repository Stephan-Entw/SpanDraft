using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;

namespace SpanDraft.Tests;

internal static class ProbeTestProjects
{
    internal static EditorDocument Document() => DesktopLayoutFixture.Document().WithSupports([
        new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
        new(Guid.NewGuid(), Length.FromMeters(1), SupportType.Roller, "B")]).WithLoads([
        new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(333.37), Force.FromNewtons(-6000), "F1"),
        new EditorPointMoment(Guid.NewGuid(), Length.FromMillimeters(333.37), Moment.FromNewtonMeters(3000), "M1")]);
}

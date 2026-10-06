using Avalonia;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Tests;

internal static class DesktopLayoutFixture
{
    public static Size Measure(string text) => new(text.Length * 7, 16);
    public static EditorDocument Document(double length = 1) => new(Length.FromMeters(length), ProjectTemplates.Material, ProjectTemplates.Section);
    public static BeamLayoutFrame Fit(double width, double height, double length) => new BeamLayoutState().Update(Document(length), width, height)!;
    public static BeamLayoutFrame Linear(double left, double right, double beamY, double length) =>
        new(StationLayout.Compute(length, left, right, []), new(right, beamY * 2, beamY));
}

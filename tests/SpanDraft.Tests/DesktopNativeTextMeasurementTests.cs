using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Skia;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

[CollectionDefinition("Schematic text", DisableParallelization = true)]
public sealed class SchematicTextCollection;

[Collection("Schematic text")]
public sealed class DesktopNativeTextMeasurementTests
{
    static DesktopNativeTextMeasurementTests() => AppBuilder.Configure<Application>()
        // No native windows or synthetic font metrics: initialize only runtime
        // services and the already referenced real Skia text backend.
        .UseWindowingSubsystem(() => { }, "Text measurement only")
        .UseStandardRuntimePlatformSubsystem().UseSkia().UseHarfBuzz().SetupWithoutStarting();

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void FormattingThenRealMeasurementFeedsPurePackerWithFiniteBounds(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var layout = StationLayout.Compute(1.4005, 72, 900,
                [new(1.2, 4.75, 4.75), new(1.25, 4.75, 4.75), new(1.3, 4.75, 4.75)]);
            var measured = new List<(string Text, Size Size)>();
            Size Measure(string text)
            {
                var size = SchematicText.Measure(text, Typeface.Default, 13);
                measured.Add((text, size));
                return size;
            }
            var axis = CoordinateAxisLayout.Create(layout, 1000, Measure);
            Assert.Equal(layout.Stations.Count, measured.Count);
            Assert.Equal(layout.Stations.Select(s => UiNumbers.Format(s.PhysicalX * 1000)), measured.Select(m => m.Text));
            Assert.All(measured, m =>
            {
                Assert.True(double.IsFinite(m.Size.Width) && m.Size.Width > 0);
                Assert.True(double.IsFinite(m.Size.Height) && m.Size.Height > 0);
            });
            Assert.All(axis.Packing.Labels, label =>
            {
                Assert.True(double.IsFinite(label.Left) && double.IsFinite(label.Right));
                int index = checked((int)label.Requirement.StableOrderKey);
                Assert.Equal(measured[index].Size.Width, label.Requirement.MeasuredWidth);
            });
            Assert.True(axis.Packing.LaneCount >= 1 && axis.Packing.LaneCount <= layout.Stations.Count);
            Assert.True(double.IsFinite(axis.PaneHeight) && axis.PaneHeight > 0);
            Assert.Same(layout.Transform, axis.StationLayout.Transform);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void RealMeasuredEntityLabelsHaveFiniteBoundsAndCarryStableNames()
    {
        var load = EditorPointLoad.Create(Guid.NewGuid(), SpanDraft.Core.Units.Length.FromMillimeters(500.5),
            PointLoadKind.Force, -1000.25, "Motorlast");
        var document = DesktopLayoutFixture.Document().WithLoads([load]);
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        var scene = BeamRenderState.Create(document, frame, text => SchematicText.Measure(text, Typeface.Default, 13));
        var label = Assert.Single(scene.Annotations);
        Assert.Equal("Motorlast = " + UiNumbers.Format(load.Value) + " N", label.Text);
        Assert.True(double.IsFinite(label.Bounds.X) && double.IsFinite(label.Bounds.Y));
        Assert.True(double.IsFinite(label.Bounds.Width) && label.Bounds.Width > 0);
        Assert.True(double.IsFinite(label.Bounds.Height) && label.Bounds.Height > 0);
        Assert.Equal(load.Id, scene.HitTestLabel(label.Bounds.Center.X, label.Bounds.Center.Y)!.Id);
    }
}

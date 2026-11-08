using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;

namespace SpanDraft.Tests;

public abstract class DesktopDistributedLoadRenderingTestSupport
{
    protected static DistributedLoadVisual Visual(double width = 96, double q = -500) =>
        new(Guid.NewGuid(), new(Length.FromMeters(.2), Length.FromMeters(.8), q), "q1", 50, 50 + width, 100, false);
}

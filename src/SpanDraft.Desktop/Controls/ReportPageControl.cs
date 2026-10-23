using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using SpanDraft.Reporting;

namespace SpanDraft.Desktop.Controls;

public sealed class ReportPageControl : Control
{
    private ReportPagePicture? page;

    static ReportPageControl() => AffectsRender<ReportPageControl>(BoundsProperty);

    internal ReportPagePicture? Page
    {
        get => page;
        set { if (ReferenceEquals(page, value)) return; page = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (page is not null && Bounds.Width > 0 && Bounds.Height > 0)
            context.Custom(new PageDrawOperation(page, new Rect(Bounds.Size)));
    }

    internal sealed class PageDrawOperation(ReportPagePicture page, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;
        public bool HitTest(Point point) => Bounds.Contains(point);
        public bool Equals(ICustomDrawOperation? other) => other is PageDrawOperation operation
            && ReferenceEquals(page, operation.Page) && Bounds == operation.Bounds;
        private ReportPagePicture Page => page;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            using var restore = new SKAutoCanvasRestore(canvas);
            canvas.ClipRect(SKRect.Create((float)Bounds.Width, (float)Bounds.Height));
            canvas.Scale((float)(Bounds.Width / ReportDocument.PageWidth), (float)(Bounds.Height / ReportDocument.PageHeight));
            page.Draw(canvas);
        }

        // The workspace owns pictures; queued operations may outlive it.
        public void Dispose() { }
    }
}

internal sealed class ReportPagePicture(SKPicture picture) : IDisposable
{
    private readonly object gate = new();
    private SKPicture? picture = picture;

    internal void Draw(SKCanvas canvas)
    {
        lock (gate)
        {
            if (picture is not null) canvas.DrawPicture(picture);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            picture?.Dispose(); picture = null;
        }
    }
}

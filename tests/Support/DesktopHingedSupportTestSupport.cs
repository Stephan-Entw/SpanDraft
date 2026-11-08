using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Tests;

public abstract class DesktopHingedSupportTestSupport
{
    protected static EditorSupport Support(SupportType type, double position = .5) =>
        new(Guid.NewGuid(), Length.FromMeters(position), type, "A");
    protected static EditorDocument Document(EditorSupport support) => DesktopLayoutFixture.Document().WithSupports([support]);
    protected static BeamRenderState Scene(EditorDocument document, BeamLayoutFrame frame, EditorViewModel? editor = null,
        EditorPresentationState? presentation = null) => BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure,
            editor?.Preview, editor?.HiddenSupportId, editor?.SupportPreviewName, presentation: presentation);
}

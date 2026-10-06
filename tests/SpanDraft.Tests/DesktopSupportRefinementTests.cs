using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopSupportRefinementTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private sealed class Session
    {
        public EditorViewModel Editor { get; }
        public int Calls { get; private set; }

        public Session(params double[] positions)
        {
            Editor = new(new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
                positions.Select((p, i) => new EditorSupport(Guid.NewGuid(), Mm(p), SupportType.Fixed, "Fixture support " + i))), () => { },
                beam => { Calls++; return BeamAnalysis.Analyze(beam); });
        }
        public void Reject(string text = "700")
        {
            Editor.DimensionLength.Begin();
            Editor.DimensionLength.Text = text;
            Assert.False(Editor.DimensionLength.Confirm());
        }
    }

    [Fact]
    public void ConflictMarksOnlyBlockingIdsWithoutDocumentChangeOrAnalysis()
    {
        var s = new Session(200, 700, 800, 850);
        var document = s.Editor.Document;
        s.Reject();
        var conflict = s.Editor.ConstraintConflict!;
        Assert.Equal(Mm(700), conflict.RequestedLength);
        Assert.Equal(document.Supports.Skip(2).Select(p => p.Id), conflict.BlockingEntityIds);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void SingleConflictAndCollectionAreTransientAndDefensivelyProtected()
    {
        var s = new Session(850);
        s.Reject();
        Assert.Equal(s.Editor.Document.Supports[0].Id, Assert.Single(s.Editor.ConflictEntityIds));
        Guid[] ids = [Guid.NewGuid()];
        var conflict = new ConstraintConflictState(Mm(700), ids);
        var original = ids[0];
        ids[0] = Guid.NewGuid();
        Assert.Equal(original, Assert.Single(conflict.BlockingEntityIds));
        Assert.Throws<NotSupportedException>(() => ((IList<Guid>)conflict.BlockingEntityIds).Clear());
    }

    [Theory]
    [InlineData("850")]
    [InlineData("900")]
    [InlineData("1000")]
    [InlineData("invalid")]
    public void CorrectedOrInvalidBufferClearsStaleConflictWithoutCommitting(string text)
    {
        var s = new Session(850);
        var document = s.Editor.Document;
        s.Reject();
        s.Editor.DimensionLength.Text = text;
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.Empty(s.Editor.ConflictEntityIds);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void ConflictTracksChangedRejectedRequestAndClearsOnAcceptedCommit()
    {
        var s = new Session(800, 850);
        s.Reject();
        s.Editor.DimensionLength.Text = "825";
        Assert.Equal(s.Editor.Document.Supports[1].Id, Assert.Single(s.Editor.ConflictEntityIds));
        Assert.Equal(Mm(825), s.Editor.ConstraintConflict!.RequestedLength);
        s.Editor.DimensionLength.Text = "900";
        Assert.True(s.Editor.DimensionLength.Confirm());
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.Equal(Mm(900), s.Editor.Document.Length);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCancelClearsConflictBeforeOrAfterRejectedFocusLoss(bool loseFocus)
    {
        var s = new Session(850);
        var document = s.Editor.Document;
        s.Reject();
        if (loseFocus)
        {
            s.Editor.DimensionLength.LoseFocus();
            Assert.False(s.Editor.DimensionLength.IsEditing);
            Assert.Equal("1000", s.Editor.DimensionLength.Text);
            Assert.False(s.Editor.DimensionLength.HasError);
            Assert.Null(s.Editor.ConstraintConflict);
            Assert.Empty(s.Editor.ConflictEntityIds);
        }
        s.Editor.DimensionLength.Cancel();
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.False(s.Editor.DimensionLength.HasError);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void NewEditSessionStartsWithoutDismissedConflict()
    {
        var s = new Session(850);
        s.Reject();
        s.Editor.DimensionLength.LoseFocus();
        Assert.Null(s.Editor.ConstraintConflict);
        s.Editor.DimensionLength.Begin();
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.False(s.Editor.DimensionLength.HasError);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void SupportCommitsReevaluateExistingConstraintWithoutExtraAnalysis()
    {
        var s = new Session(800, 850);
        s.Reject();
        s.Editor.EditSupport(s.Editor.Document.Supports[0].Id);
        s.Editor.SupportDraft!.PositionText = "600";
        Assert.True(s.Editor.ConfirmSupport());
        Assert.Single(s.Editor.ConflictEntityIds);
        Assert.True(s.Editor.DimensionLength.HasError);
        Assert.Equal(2, s.Calls);
        s.Editor.EditSupport(s.Editor.Document.Supports[1].Id);
        s.Editor.DeleteSupport();
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.False(s.Editor.DimensionLength.HasError);
        Assert.Equal(3, s.Calls);
    }

    [Theory]
    [InlineData(-10000, 0)]
    [InlineData(10000, 1000.5)]
    public void CapturedDragClampsOutsideSurfaceAndReleaseOpensValidDraft(double pointer, double expected)
    {
        var s = new Session(200);
        s.Editor.DimensionLength.Begin();
        s.Editor.DimensionLength.Text = UiNumbers.Format(1000.5);
        Assert.True(s.Editor.DimensionLength.Confirm());
        var document = s.Editor.Document;
        var support = document.Supports[0];
        var v = DesktopLayoutFixture.Linear(72, 1178, 200, document.Length.Meters);
        var gesture = new SupportDragGesture(support.Id, support.Position, v.Layout.Transform.PhysicalToScreen(support.Position.Meters) + 8, v.Layout.Transform);
        var snapped = gesture.Update(pointer, 200, 500);
        Assert.Equal(Mm(expected), snapped);
        Assert.True(s.Editor.BeginSupportDrag(support.Id));
        s.Editor.UpdateSupportDrag(snapped);
        Assert.False(s.Editor.Preview!.IsInvalid);
        Assert.False(s.Editor.HasSupportFeedback);
        Assert.True(s.Editor.EndSupportDrag());
        Assert.True(s.Editor.SupportDraft!.IsValid);
        Assert.Equal(Mm(expected), s.Editor.Preview!.Position);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(2, s.Calls);
        Assert.True(s.Editor.ConfirmSupport());
        Assert.Equal(support.Id, s.Editor.Document.Supports[0].Id);
        Assert.Equal(3, s.Calls);
    }

    [Fact]
    public void DragReturnsImmediatelyToNormalSnapWithGrabOffset()
    {
        var v = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
        var gesture = new SupportDragGesture(Guid.NewGuid(), Mm(200), 208, v.Layout.Transform);
        Assert.Equal(Mm(1000), gesture.Update(10000, 200, 500));
        Assert.Equal(Mm(207), gesture.Update(215.4, 200, 500));
        Assert.Equal(Mm(208), gesture.Update(215.5, 200, 500));
        Assert.Equal(Mm(0), gesture.Update(-10000, 200, 500));
        Assert.Equal(Mm(400), gesture.Update(408, 200, 500));
        Assert.Null(gesture.Update(408, 501, 500));
    }

    [Fact]
    public void ClampedEndpointCollisionRemainsInvalidAndManualTextNeverClamps()
    {
        var s = new Session(200, 1000);
        var document = s.Editor.Document;
        var support = document.Supports[0];
        var v = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
        var gesture = new SupportDragGesture(support.Id, support.Position, 200, v.Layout.Transform);
        s.Editor.BeginSupportDrag(support.Id);
        s.Editor.UpdateSupportDrag(gesture.Update(5000, 200, 500));
        Assert.True(s.Editor.Preview!.IsInvalid);
        Assert.Equal(Strings.SupportAlreadyExists, s.Editor.SupportFeedback);
        Assert.False(s.Editor.EndSupportDrag());
        s.Editor.EditSupport(support.Id);
        s.Editor.SupportDraft!.PositionText = "1200";
        Assert.False(s.Editor.SupportDraft.IsValid);
        Assert.False(s.Editor.ConfirmSupport());
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void LengthBufferRemainsRawWithoutFormattingCommitOrAnalysisWhileTyping()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var s = new Session();
            var document = s.Editor.Document;
            var input = s.Editor.DimensionLength;
            input.Begin();
            foreach (var text in new[] { "1", "12", "1234,", "01234,500" })
            {
                input.Text = text;
                Assert.Equal(text, input.Text);
                Assert.Same(document, s.Editor.Document);
                Assert.Equal(1, s.Calls);
            }
            Assert.True(input.Confirm());
            Assert.Equal("1234,5", input.Text);
            Assert.Equal(2, s.Calls);
            input.Begin();
            input.Text = "abc";
            Assert.False(input.Confirm());
            Assert.Equal("abc", input.Text);
            input.Cancel();
            Assert.Equal("1234,5", input.Text);
            Assert.Equal(2, s.Calls);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}

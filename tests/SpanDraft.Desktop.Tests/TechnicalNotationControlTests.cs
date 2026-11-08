using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Core.Units;
using SpanDraft.Reporting.Notation;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class TechnicalNotationControlTests
{
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void EditorPositionLabelsUseLowercaseTechnicalXAndKeepLoadLabelsAndNamesPlain(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var fixture = ReportTestProjects.Create().Document;
        var moment = new EditorPointMoment(Guid.NewGuid(), Length.FromMillimeters(400), Moment.FromNewtonMeters(1), "M1");
        var document = fixture.WithLoads([.. fixture.Loads, moment]);
        var editor = new EditorViewModel(document, () => { });
        Assert.True(editor.EditSupport(document.Supports[0].Id));
        CheckPosition(new SupportFlyout { DataContext = editor }, "PositionInput", "Position x");
        editor.CancelEditorInteraction();
        foreach (var load in document.Loads)
        {
            Assert.True(editor.EditLoad(load.Id));
            var view = new PointLoadFlyout { DataContext = editor };
            CheckPosition(view, "PositionInput", "Position x");
            CheckPlain(view, load.Kind == PointLoadKind.Force ? Strings.ForceValueLabel : Strings.MomentValueLabel);
            Assert.Equal(load.Name, editor.LoadDraft!.NameText);
            editor.CancelEditorInteraction();
        }
        Assert.True(editor.EditDistributedLoad(document.DistributedLoads[0].Id));
        var distributed = new DistributedLoadFlyout { DataContext = editor };
        CheckPosition(distributed, "StartInput", "Start x");
        CheckPosition(distributed, "EndInput", culture == "de-DE" ? "Ende x" : "End x");
        CheckPlain(distributed, Strings.DistributedIntensityLabel);
        Assert.Equal("q1", editor.DistributedLoadDraft!.NameText);
        editor.CancelEditorInteraction();
        Assert.Same(document, editor.Document);

        static void CheckPosition(Control view, string inputName, string expected)
        {
            var window = new Window { Content = view, UseLayoutRounding = false };
            window.ApplyTemplate();
            window.Measure(new Size(360, 600));
            window.Arrange(new Rect(0, 0, 360, 600));
            var input = view.FindControl<TextBox>(inputName)!;
            var label = Assert.Single(((Grid)input.Parent!).Children.OfType<TextBlock>(),
                text => TechnicalTextPresenter.GetContent(text) is not null);
            var notation = TechnicalTextPresenter.GetContent(label)!;
            Assert.Equal(expected, notation.PlainText);
            Assert.Equal(TechnicalSymbols.Axis("x").Runs, notation.Runs.TakeLast(1));
            Assert.Equal(FontStyle.Normal, ((Run)label.Inlines![0]).FontStyle);
            Assert.Equal(FontStyle.Italic, ((Run)label.Inlines.Last()).FontStyle);
            Assert.Equal(expected, new TextBlockAutomationPeer(label).GetName());
            Assert.Equal(expected, AutomationProperties.GetName(input));
            Assert.True(label.TextLayout.Width <= label.Bounds.Width + .01);
            Assert.True(label.TextLayout.Height <= label.Bounds.Height + .01);
            window.Content = null;
            window.Close();
        }
        static void CheckPlain(Control view, string expected)
        {
            var label = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == expected);
            Assert.Null(TechnicalTextPresenter.GetContent(label));
            Assert.Equal(FontStyle.Normal, label.FontStyle);
        }
    }

    [Fact]
    public void RebindingAndRemovingNotationKeepNativeAccessibilityText()
    {
        var block = new TextBlock();
        var peer = new TextBlockAutomationPeer(block);
        TechnicalTextPresenter.SetContent(block, TechnicalSymbols.YieldStrength);
        Assert.Equal("Re", peer.GetName());
        TechnicalTextPresenter.SetContent(block, TechnicalSymbols.ProofStrength);
        Assert.Equal("Rp0,2", peer.GetName());
        AutomationProperties.SetName(block, "Material strength");
        TechnicalTextPresenter.SetContent(block, TechnicalSymbols.YieldStrength);
        Assert.Equal("Material strength", AutomationProperties.GetName(block));
        Assert.Equal("Re", peer.GetName());
        TechnicalTextPresenter.SetContent(block, null);
        Assert.Empty(block.Inlines!);
        Assert.Equal("Material strength", AutomationProperties.GetName(block));
        block.ClearValue(AutomationProperties.NameProperty);
        block.Text = "Plain text";
        Assert.Equal("Plain text", peer.GetName());
    }
}

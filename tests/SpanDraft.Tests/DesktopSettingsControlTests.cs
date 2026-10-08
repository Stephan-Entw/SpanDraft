using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopSettingsControlTests
{
    [Theory]
    [InlineData("de-DE", 900, 780)]
    [InlineData("en-US", 900, 780)]
    [InlineData("de-DE", 820, 450)]
    [InlineData("en-US", 820, 450)]
    public void DialogKeepsOneScrollablePageWithAlignedFormsAndReachableActions(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        var window = new SettingsWindow(model);
        Arrange(window, width, height);
        var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
        Assert.False(window.FindControl<ComboBox>("LanguagePicker")!.IsEnabled);
        Assert.Null(window.FindControl<Button>("ApplyButton"));
        Assert.True(window.FindControl<Button>("OkButton")!.IsEnabled);
        Assert.Equal(Strings.OK, window.FindControl<Button>("OkButton")!.Content);
        Assert.Equal(Strings.Cancel, window.FindControl<Button>("CancelButton")!.Content);
        Assert.False(window.FindControl<RadioButton>("CustomProfileOption")!.IsVisible);
        var profiles = window.FindControl<StackPanel>("ProfileRow")!;
        var radios = profiles.Children.OfType<RadioButton>().Where(r => r.IsVisible).ToArray();
        Assert.Equal(3, radios.Length);
        Assert.True(radios[1].Bounds.Left >= radios[0].Bounds.Right);
        Assert.True(radios[2].Bounds.Left >= radios[1].Bounds.Right);
        Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());
        foreach (var radio in window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == "Presentation"))
        {
            var description = radio.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("secondary"));
            var origin = description.TranslatePoint(default, radio)!.Value;
            Assert.True(origin.Y + description.Bounds.Height <= radio.Bounds.Height);
            Assert.True(description.Bounds.Height >= description.TextLayout.Height);
        }
        foreach (string name in new[] { "LeftUnitList", "RightUnitList" })
        {
            var list = window.FindControl<ItemsControl>(name)!;
            var combos = list.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToArray();
            Assert.Equal(name == "LeftUnitList" ? 7 : 5, combos.Length);
            Assert.All(combos, c => Assert.Equal(132, c.Bounds.Width, 6));
            double x = combos[0].TranslatePoint(default, list)!.Value.X;
            Assert.All(combos, c => Assert.Equal(x, c.TranslatePoint(default, list)!.Value.X, 6));
        }
        Export(scroll, $"settings-{culture}-{width}x{height}-top.png", width, height);
        model.ChangeUnit(QuantityKind.BeamLength, UnitCatalog.Inch);
        Arrange(window, width, height);
        Assert.True(window.FindControl<RadioButton>("CustomProfileOption")!.IsVisible);
        Assert.True(window.FindControl<Button>("OkButton")!.IsEnabled);
        if (height == 450)
        {
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.Offset = new(0, scroll.Extent.Height);
            Arrange(window, width, height);
            var button = window.FindControl<Button>("OkButton")!;
            var origin = button.TranslatePoint(default, scroll)!.Value;
            Assert.InRange(origin.Y, 0, scroll.Viewport.Height - button.Bounds.Height);
        }
        Export(scroll, $"settings-{culture}-{width}x{height}-custom.png", width, height);
    }

    [Fact]
    public void OkSavesChangesAndClosesTheWindow()
    {
        using var environment = new DesktopControlEnvironment();
        UserSettings? published = null;
        var model = new SettingsViewModel(UserSettings.Default, settings =>
        { published = settings; return Task.FromResult<string?>(null); });
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.IsDetailed = true;
        Click(window, "OkButton");
        Assert.True(closeApproved);
        Assert.Equal(PresentationMode.Detailed, published!.Mode);
        Assert.True(model.IsDetailed); Assert.False(model.HasChanges);
    }

    [Fact]
    public void UnchangedOkClosesWithoutSaving()
    {
        using var environment = new DesktopControlEnvironment();
        int writes = 0;
        var model = new SettingsViewModel(UserSettings.Default, _ =>
        { writes++; return Task.FromResult<string?>(null); });
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        Click(window, "OkButton");
        Assert.True(closeApproved); Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("close")]
    public void CancelEscapeAndCloseDiscardPendingChangesWithoutSaving(string action)
    {
        using var environment = new DesktopControlEnvironment();
        int writes = 0;
        var model = new SettingsViewModel(UserSettings.Default, _ =>
        { writes++; return Task.FromResult<string?>(null); });
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.Select(UnitProfileKind.UnitedStates); model.IsDetailed = true;
        if (action == "cancel") Click(window, "CancelButton");
        else if (action == "escape") Escape(window);
        else window.Close();
        Assert.True(closeApproved); Assert.Equal(0, writes);
        Assert.True(model.Pending.ContentEquals(UserSettings.Default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedOkKeepsWindowAndPendingChangesForCorrectionOrCancel(bool activeDraft)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new ProjectTestSupport.Files();
        if (!activeDraft) files.FailWritePath = "settings";
        var main = new MainWindowViewModel(files: files,
            settingsStore: new SpanDraft.Desktop.Persistence.LocalSettingsStore(files, "settings"));
        if (activeDraft)
        {
            main.Setup.ApplyCommand.Execute(null);
            main.Editor!.DimensionLength.Begin(); main.Editor.DimensionLength.Text = "1200";
        }
        var model = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.Select(UnitProfileKind.UnitedStates);
        Click(window, "OkButton");
        Assert.False(closeApproved); Assert.True(model.IsUnitedStates); Assert.True(model.HasError);
        Assert.True(main.Settings.ContentEquals(UserSettings.Default));
        if (activeDraft) main.Editor!.DimensionLength.Cancel();
        else files.FailWritePath = null;
        Click(window, "OkButton");
        Assert.True(closeApproved); Assert.True(main.Settings.ContentEquals(model.Pending));
    }

    [Fact]
    public void SavingRejectsRepeatedOkCancelEscapeAndCloseThenClosesOnSuccess()
    {
        using var environment = new DesktopControlEnvironment();
        var completion = new TaskCompletionSource<string?>();
        int writes = 0;
        var model = new SettingsViewModel(UserSettings.Default, _ => { writes++; return completion.Task; });
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.IsDetailed = true;
        Click(window, "OkButton");
        Assert.True(model.IsSaving);
        Assert.False(window.FindControl<Button>("OkButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("CancelButton")!.IsEnabled);
        Click(window, "OkButton"); Click(window, "CancelButton"); Escape(window); window.Close();
        Assert.False(closeApproved); Assert.True(model.IsDetailed); Assert.Equal(1, writes);
        completion.SetResult(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closeApproved); Assert.False(model.IsSaving); Assert.False(model.HasChanges);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void UnitChangesRefreshTextsWithoutChangingStationsGlyphAnchorsOrDiagramSources(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        int analyses = 0;
        var document = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Fixed, "A")],
            [new EditorPointForce(Guid.NewGuid(), Length.FromMeters(.4), Force.FromNewtons(-1000), "F1"),
             new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(.6), Moment.FromNewtonMeters(50), "M1")],
            distributedLoads: [new(Guid.NewGuid(), Length.FromMeters(.2), Length.FromMeters(.8), ForcePerLength.FromNewtonsPerMeter(-500), "q1")]);
        var main = new MainWindowViewModel(b => { analyses++; return SpanDraft.Analysis.BeamAnalysis.Analyze(b); });
        main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!;
        editor.Session.Commit(new(document, new()));
        int initialAnalyses = analyses;
        var view = new EditorView { DataContext = editor }; var window = new Window { Content = view };
        Arrange(window, 1250, 800);
        var surface = view.GetVisualDescendants().OfType<BeamEditorSurface>().Single();
        var canvas = view.GetVisualDescendants().OfType<BeamCanvas>().Single();
        var axis = view.GetVisualDescendants().OfType<CoordinateAxisPane>().Single();
        var layout = surface.StationLayout!; var frame = canvas.Scene!.Frame;
        var positions = canvas.Scene.Loads.Select(l => l.X).ToArray();
        var result = editor.Presentation.Result;
        var revision = editor.Session.CurrentRevision;
        foreach (var profile in new[] { UnitProfile.StructuralEngineering, UnitProfile.UnitedStates, DesktopResultPresentationTests.Mixed, UnitProfile.Default })
        {
            Assert.True(main.SetResultPresentation(profile, PresentationMode.Standard));
            Arrange(window, 1250, 800);
            Assert.Same(layout, surface.StationLayout);
            Assert.Equal(frame.Viewport, canvas.Scene!.Frame.Viewport);
            Assert.Equal(positions, canvas.Scene.Loads.Select(l => l.X));
            Assert.Equal(profile[QuantityKind.BeamLength].Symbol, axis.AxisLayout!.UnitSymbol);
            Assert.Equal(profile[QuantityKind.BeamLength].Symbol, editor.DimensionLength.Unit);
            Assert.Same(revision, editor.Session.CurrentRevision);
            Assert.Same(result, editor.Presentation.Result);
            Assert.Equal(initialAnalyses, analyses);
            Assert.Contains(canvas.Scene.Annotations, a => a.Text == PointLoadSymbol.Label(new(document.Loads[0].Position,
                PointLoadKind.Force, -1000), "F1", profile));
            Assert.All(view.GetVisualDescendants().OfType<ResultDiagram>(), d => Assert.Same(layout, d.Projection!.StationLayout));
        }
    }

    private static void Click(SettingsWindow window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Escape(SettingsWindow window) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

    private static void Arrange(Window window, double width, double height)
    {
        window.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            foreach (var child in content.GetVisualDescendants().OfType<Control>())
            { child.InvalidateMeasure(); child.InvalidateArrange(); }
            content.InvalidateMeasure(); content.InvalidateArrange();
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Export(Control control, string name, double width, double height)
    {
        if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
        bitmap.Render(control); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

using static SpanDraft.Tests.DesktopControlEnvironment;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SectionDefinitionControlTests : SectionDefinitionPrototypeTestSupport
{
    [Fact]
    public void RealFormButtonsAxesAndInlineNameKeyboardWorkWithoutClosingTheHost()
    {
        using var environment=new DesktopControlEnvironment();
        var vm=new SectionDefinitionViewModel();var view=new SectionDefinitionView{DataContext=vm};var window=new Window{Content=view};window.Show();ArrangeSection(window);
        Assert.Equal(8,view.GetVisualDescendants().OfType<SectionShapeIcon>().Count(c=>c.IsEffectivelyVisible));
        var angle=view.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,SectionEditorType.Angle));
        angle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ArrangeSection(window);
        Assert.Equal(SectionEditorType.Angle,vm.Type);Assert.False(view.FindControl<StackPanel>("FormChooser")!.IsEffectivelyVisible);
        Fill(vm, false); ArrangeSection(window);
        var axis=view.GetVisualDescendants().OfType<SectionAxisBlock>().Last(c=>c.IsEffectivelyVisible);
        axis.Select();Assert.Equal(SectionAxisDesignation.V,vm.SelectedAxis);
        vm.EditNameCommand.Execute(null);Dispatcher.UIThread.RunJobs();
        var name=view.FindControl<TextBox>("NameInput")!;name.Text="Temporary";Dispatcher.UIThread.RunJobs();
        name.SelectionStart=3;name.SelectionEnd=3;vm.PendingName="Temporary profile";Dispatcher.UIThread.RunJobs();Assert.Equal(name.SelectionStart,name.SelectionEnd);
        name.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Assert.False(vm.IsEditingName);Assert.NotEqual("Temporary",vm.DisplayName);
        vm.EditNameCommand.Execute(null);Dispatcher.UIThread.RunJobs();name.Text="My angle";
        name.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Assert.Equal("My angle",vm.DisplayName);
        vm.ChangeFormCommand.Execute(null);Assert.True(vm.IsChoosingForm);
        view.FindControl<Button>("ManualFormButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ArrangeSection(window);
        Assert.True(vm.IsManual);Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);window.Close();
    }
    [Fact]
    public void ProductTextBoxesCommitOnEnterAndLostFocusAndDoNotSubmitTheDialog()
    {
        using var environment=new DesktopControlEnvironment();
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        var view=new SectionDefinitionView{DataContext=vm}; var window=new Window{Content=view}; window.Show(); ArrangeSection(window);
        var boxes=view.FindControl<ItemsControl>("GeometryInputList")!.GetVisualDescendants().OfType<TextBox>().ToArray();
        var b=boxes.Single(x=>((SectionDefinitionInput)x.DataContext!).Key=="b");
        var h=boxes.Single(x=>((SectionDefinitionInput)x.DataContext!).Key=="h");
        int closed=0; vm.CloseRequested+=_=>closed++;
        b.Focus(); b.Text="60"; var old=vm.Preview; Assert.Same(old,vm.Preview);
        b.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
        Assert.Equal(.06,Input(vm,"b").ConfirmedValue); Assert.NotSame(old,vm.Preview); Assert.Equal(0,closed);
        h.Focus(); h.Text="120"; var partial=vm.Preview; Assert.Same(partial,vm.Preview);
        view.FindControl<Button>("TitleButton")!.Focus(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(.12,Input(vm,"h").ConfirmedValue); Assert.False(vm.CanConfirm); vm.SelectedAxis=SectionAxisDesignation.Y; Assert.True(vm.CanConfirm); Assert.Equal(0,closed);
        window.Close();
    }
    [Theory]
    [InlineData("de-DE", SectionEditorType.ISection)] [InlineData("en-US", SectionEditorType.ISection)]
    [InlineData("de-DE", SectionEditorType.Manual)] [InlineData("en-US", SectionEditorType.Manual)]
    public void NumericCaretTracksTypedGlyphsAndKeepsItsInsertionPosition(string culture, SectionEditorType type)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view };
        window.Show(); ArrangeSection(window, 1000, 600);
        var box = view.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.IsEffectivelyVisible && t.DataContext is SectionDefinitionInput);
        var input = Assert.IsType<SectionDefinitionInput>(box.DataContext); var preview = vm.Preview;
        box.Focus(); box.ClearSelection(); box.CaretIndex = 0;
        var presenter = box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
        // Read the actual drawn caret, not just the logical index. Avalonia keeps
        // these screen coordinates internal; no product test hook is needed.
        var getCaretPoints = presenter.GetType().GetMethod("GetCaretPoints",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Point Caret() => ((ValueTuple<Point, Point>)getCaretPoints.Invoke(presenter, null)!).Item1;
        foreach (string digit in new[] { "4", "5", "6" })
        {
            Type(digit); Assert.Equal(input.PendingText.Length, box.CaretIndex);
            var top = Caret();
            // The one-pixel caret is snapped and inset by Avalonia. Compare to
            // the actual glyph position with room for that raster alignment.
            Assert.InRange(Math.Abs(top.X - presenter.TextLayout.HitTestTextPosition(box.CaretIndex).X), 0, 2);
            Assert.Same(preview, vm.Preview); Assert.Null(input.ConfirmedValue);
        }
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
        Type("7"); Assert.Equal("4576", input.PendingText); Assert.Equal(3, box.CaretIndex);
        var caret = Caret();
        var expected = presenter.TextLayout.HitTestTextPosition(box.CaretIndex);
        Assert.InRange(Math.Abs(caret.X - expected.X), 0, 2);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
        ArrangeSection(window, 1000, 600); Assert.Equal("456", input.PendingText); Assert.Equal(2, box.CaretIndex);
        box.SelectAll(); Type("12"); Assert.Equal("12", input.PendingText); Assert.Equal(2, box.CaretIndex);
        Assert.InRange(Math.Abs(Caret().X - presenter.TextLayout.HitTestTextPosition(box.CaretIndex).X), 0, 2);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.NotNull(input.ConfirmedValue); Assert.Equal(2, box.CaretIndex);
        window.Close();

        void Type(string text)
        {
            box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
            ArrangeSection(window, 1000, 600);
        }
    }
    [Theory]
    [InlineData("de-DE",1250,800)] [InlineData("en-US",1250,800)]
    [InlineData("de-DE",1100,650)] [InlineData("en-US",1100,650)]
    public void ProductionControlsMeasureRequestedStatesAndManualModeReflows(string culture,double width,double height)
    {
        using var environment=new DesktopControlEnvironment(); using var scope=new UiCultureScope(culture);
        var vm=new SectionDefinitionViewModel(); var view=new SectionDefinitionView { DataContext=vm };
        var window=new Window { Content=view }; var owner=new Window(); owner.Show(); window.Show();
        Capture("selection");
        foreach (var type in new[]{SectionEditorType.Rectangle,SectionEditorType.RectangularHollow,SectionEditorType.ISection,SectionEditorType.Angle})
        { vm.SelectForm(type); Capture(type+"-partial"); Fill(vm); Capture(type.ToString()); }
        vm.SelectForm(SectionEditorType.Manual); Fill(vm); Capture("manual-y");
        AssertManual(); vm.Configuration=vm.Configurations.Single(c=>c.Value==ManualAxisConfiguration.YZ); Fill(vm); Capture("manual-yz"); AssertManual();
        vm.Configuration=vm.Configurations.Single(c=>c.Value==ManualAxisConfiguration.UV); Fill(vm); Capture("manual-uv");
        vm.SelectForm(SectionEditorType.ISection); Commit(vm,"h","300"); Commit(vm,"b","280"); Commit(vm,"tw","1"); Capture("thin-web");
        Commit(vm,"h","2000"); Commit(vm,"b","120"); Capture("tall");
        Commit(vm,"h","200"); Commit(vm,"b","2000"); Capture("wide");
        window.Close(); owner.Close();
        void AssertManual()
        {
            Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
            Assert.Equal(2,view.FindControl<Grid>("DefinitionColumns")!.ColumnDefinitions.Count);
            Assert.Equal(1,Grid.GetColumn(view.FindControl<Grid>("ValuesColumn")!));
            Assert.True(view.FindControl<ItemsControl>("ManualInputList")!.IsEffectivelyVisible);
            Assert.All(view.FindControl<ItemsControl>("ManualInputList")!.GetVisualDescendants().OfType<TextBox>(),box=>Assert.False(box.IsReadOnly));
        }
        void Capture(string name)
        {
            ArrangeSection(window,width,height);
            var columns = view.FindControl<Grid>("DefinitionColumns")!;
            var visibleColumns = columns.Children.OfType<Control>().Where(c => c.IsEffectivelyVisible).OrderBy(c => c.Bounds.Left).ToArray();
            for (int i = 1; i < visibleColumns.Length; i++)
                Assert.True(visibleColumns[i].Bounds.Left >= visibleColumns[i - 1].Bounds.Right, name + ": overlapping columns");
            foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>().Where(s=>s.IsEffectivelyVisible&&s.FindAncestorOfType<TextBox>() is null))
                Assert.True(scroll.Extent.Width<=scroll.Viewport.Width+1,name+": horizontal overflow");
            foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.IsEffectivelyVisible&&t.Bounds.Width>0&&t.Text?.Length>0))
                Assert.True(text.Bounds.Height+1>=text.TextLayout.Height,name+": clipped text "+text.Text);
            var confirm=view.FindControl<Button>("CancelButton")!; var point=confirm.TranslatePoint(default,view)!.Value;
            Assert.InRange(point.Y,0,height-confirm.Bounds.Height); Assert.InRange(point.X,0,width-confirm.Bounds.Width);

        }
    }
    [Theory]
    [InlineData(false, 1100, 650)] [InlineData(true, 1250, 800)]
    [InlineData(false, 1400, 900)] [InlineData(true, 1400, 900)]
    public async Task SectionCardUsesExistingWindowAndOnlyUpdatesTheDraftOnAcceptance(bool accept, double width, double height)
    {
        using var environment=new DesktopControlEnvironment();
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
        var setup=main.Setup; var original=setup.SelectedSection; var axis=setup.BendingAxis;
        var view=new ProjectSetupView{DataContext=setup}; var owner=new Window{Content=view}; owner.Show(); ArrangeSection(owner,width,height);
        var decorations=owner.WindowDecorations; var position=owner.Position;
        main.Setup.ChangeSectionCommand.Execute(null); ArrangeSection(owner,width,height);
        var preview=Assert.Single(view.GetVisualDescendants().OfType<SectionDefinitionView>());
        Assert.Same(view,owner.Content); Assert.Empty(owner.OwnedWindows);
        var vm=Assert.IsType<SectionDefinitionViewModel>(preview.DataContext); Assert.True(vm.IsChoosingForm);
        Assert.Null(original);
        Assert.Same(setup.ResultPresentation,vm.Presentation);
        if (accept) { vm.SelectForm(SectionEditorType.Rectangle); Fill(vm); Assert.True(await vm.ConfirmAsync()); }
        else vm.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs(); Assert.Same(view,owner.Content);
        Assert.Equal(decorations,owner.WindowDecorations); Assert.Equal(position,owner.Position);
        if (accept) Assert.IsType<RectangleSectionGeometry>(setup.SelectedSection);
        else { Assert.Same(original,setup.SelectedSection); Assert.Equal(axis,setup.BendingAxis); }
        Assert.Equal(ProjectSetupPage.Overview,setup.Page); Assert.Null(main.Editor); Assert.Empty(files.Writes);
        setup.ShowSections(); Assert.NotSame(vm,setup.Definition); setup.Escape(); owner.Close();
    }
    [Fact]
    public async Task HostedDefinitionProtectsActiveLibrarySaveAndReturnsToSetup()
    {
        using var environment=new DesktopControlEnvironment();
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
        var setup=new ProjectSetupView { DataContext=main.Setup }; var owner=new Window { Content=setup }; owner.Show(); ArrangeSection(owner);
        main.Setup.ShowSections(); var vm=main.Setup.Definition!; vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        var release=new TaskCompletionSource(); var reached=new TaskCompletionSource();
        files.BeforeWrite=async (_,_,_) => { reached.SetResult(); await release.Task; };
        var pending=vm.SaveLibraryAsync(); await WaitForCheckpoint(reached.Task,pending);
        try
        {
            Assert.True(main.IsWorkspaceBusy); Assert.False(await main.RequestCloseAsync());
            Assert.Same(setup,owner.Content); Assert.False(main.NewCommand.CanExecute(null));
            vm.CancelCommand.Execute(null); Assert.Same(vm,main.Setup.CurrentStep);
        }
        finally { release.SetResult(); }
        Assert.True(await WaitFor(pending)); vm.CancelCommand.Execute(null);
        Assert.Equal(ProjectSetupPage.Overview,main.Setup.Page); Assert.Single(main.UserSections.All); owner.Close();
    }
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1000, 600)] [InlineData("en-US", 1000, 600)]
    public void HiddenPropertiesLeaveSketchAtTheSameHorizontalPositionAndWidthAsThreeColumns(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); ArrangeSection(window, width, height);
        var grid = view.FindControl<Grid>("DefinitionColumns")!;
        var inputs = (Control)grid.Children[0];
        var preview = view.FindControl<Grid>("PreviewColumn")!;
        var values = view.FindControl<Grid>("ValuesColumn")!;
        var canvas = view.FindControl<SectionPreviewControl>("SectionPreview")!;
        foreach (var form in vm.Forms)
        {
            vm.ChangeFormCommand.Execute(null); vm.SelectForm(form.Value); Layout();
            var draftLeft = preview.Bounds.Left; var draftWidth = preview.Bounds.Width;
            var inputWidth = inputs.Bounds.Width; var canvasWidth = canvas.Bounds.Width;
            var canvasLeft = canvas.TranslatePoint(default, grid)!.Value.X;
            Assert.False(values.IsEffectivelyVisible);
            Assert.True(grid.Bounds.Width - preview.Bounds.Right > grid.Bounds.Width * .25, "Hidden properties must leave the right column free");
            Fill(vm, false); Layout(); Assert.True(values.IsEffectivelyVisible); AssertStable();
            var field = vm.Parameters[0]; var valid = field.PendingText;
            field.PendingText = "invalid"; Layout(); AssertStable();
            field.Commit(); Layout(); Assert.False(values.IsEffectivelyVisible); AssertStable();
            field.PendingText = valid; field.Commit(); Layout(); Assert.True(values.IsEffectivelyVisible); AssertStable();
            vm.ChangeFormCommand.Execute(null); ArrangeSection(window, width == 1100 ? 1250 : 1100, height == 650 ? 800 : 650);
            vm.SelectForm(form.Value); Layout(); AssertStable();

            void AssertStable()
            {
                Assert.Equal(draftLeft, preview.Bounds.Left); Assert.Equal(draftWidth, preview.Bounds.Width);
                Assert.Equal(inputWidth, inputs.Bounds.Width); Assert.Equal(canvasWidth, canvas.Bounds.Width);
                Assert.Equal(canvasLeft, canvas.TranslatePoint(default, grid)!.Value.X);
            }
        }
        window.Close();

        void Layout()
        {
            ArrangeSection(window, width, height);
            Assert.Equal(3, grid.ColumnDefinitions.Count);
            Assert.True(grid.ColumnDefinitions[0].Width.IsAbsolute);
            Assert.Equal(grid.ColumnDefinitions[0].ActualWidth, inputs.Bounds.Width);
            Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 360);
            Assert.True(preview.Bounds.Width >= 180);
            Assert.InRange(canvas.Bounds.Width, 180, width);
        }
    }
    [Fact]
    public void ManualBlockSelectionDoesNotStealEnterFromEditingOrReplaceFocusedInputs()
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Manual);
        vm.Configuration = vm.Configurations.Single(c => c.Value == ManualAxisConfiguration.YZ); Fill(vm, false);
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view }; window.Show(); ArrangeSection(window);
        var list = view.FindControl<ItemsControl>("ManualInputList")!;
        Assert.All(list.GetVisualDescendants().OfType<SectionAxisBlock>(), b => { Assert.False(b.IsSelectable); Assert.False(b.Focusable); });
        Commit(vm, "WZ", "100000"); ArrangeSection(window);
        var block = list.GetVisualDescendants().OfType<SectionAxisBlock>().Last();
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(t => ((SectionDefinitionInput)t.DataContext!).Key == "WZ");
        int closed = 0; vm.CloseRequested += _ => closed++;
        block.Select(); Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis);
        input.Focus(); input.Text = "100001";
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm); Assert.True(input.IsFocused); Assert.Equal(0, closed);
        Assert.Same(input, list.GetVisualDescendants().OfType<TextBox>().Single(t => ((SectionDefinitionInput)t.DataContext!).Key == "WZ"));
        block.Focus(); block.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis); Assert.True(vm.CanConfirm); Assert.Equal(0, closed);
        input.Focus(); input.Text = "100002"; view.FindControl<Button>("TitleButton")!.Focus(); Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm); Assert.Equal(0, closed);
        Assert.Equal(1e-4 + 2e-9, Input(vm, "WZ").ConfirmedValue!.Value, 12);
        window.Close();
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void EquivalentControlBlockIsNeutralAndShowsOnlyCommonValues(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Circle); Fill(vm, false);
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view }; window.Show(); ArrangeSection(window);
        var block = Assert.Single(view.FindControl<ItemsControl>("AxisValueList")!.GetVisualDescendants().OfType<SectionAxisBlock>());
        Assert.False(block.IsSelectable); Assert.False(block.Focusable); Assert.False(block.IsSelected);
        Assert.False(view.FindControl<TextBlock>("AxisSelectionHint")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled);
        block.Select(); Assert.Null(vm.SelectedAxis); Assert.Null(view.FindControl<SectionPreviewControl>("SectionPreview")!.SelectedAxis);
        var labels = block.GetVisualDescendants().OfType<TextBlock>().Select(t => TechnicalTextPresenter.GetContent(t)?.PlainText ?? t.Text).ToArray();
        Assert.Contains(Strings.SectionDefinitionInertia + " I", labels); Assert.Contains(Strings.SectionDefinitionModulus + " W+", labels);
        Assert.Contains(Strings.SectionDefinitionModulus + " W−", labels);
        window.Close();
    }
    [Theory]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public void LiveLayoutTransitionsKeepColumnsSeparatedWithoutForcedChildInvalidation(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); ArrangeSection(window, width, height);
        var grid = view.FindControl<Grid>("DefinitionColumns")!;
        var inputs = (Control)grid.Children[0];
        var preview = view.FindControl<Grid>("PreviewColumn")!;
        var values = view.FindControl<Grid>("ValuesColumn")!;
        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
        void AssertSeparated(Control left, Control right)
        {
            Assert.True(right.Bounds.Left >= left.Bounds.Right,
                $"Overlapping columns: left={left.Bounds}; right={right.Bounds}; definitions={grid.ColumnDefinitions}");
            Assert.InRange(right.Bounds.Right, 0, grid.Bounds.Width + 1);
        }
        vm.SelectForm(SectionEditorType.Rectangle); Layout(); AssertSeparated(inputs, preview);
        Fill(vm); Layout(); AssertSeparated(inputs, preview); AssertSeparated(preview, values);
        Commit(vm, "b", "bad"); Layout(); AssertSeparated(inputs, preview);
        vm.ChangeFormCommand.Execute(null); Layout(); Assert.False(preview.IsEffectivelyVisible);
        vm.SelectForm(SectionEditorType.Manual); Layout(); AssertSeparated(inputs, values);
        vm.ChangeFormCommand.Execute(null); Layout(); vm.SelectForm(SectionEditorType.Circle); Layout(); AssertSeparated(inputs, preview);
        Fill(vm); Layout(); AssertSeparated(inputs, preview); AssertSeparated(preview, values);
        window.Close();
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void RepeatedEditingAndResizingKeepsEveryFormCommandsAndColumnInItsOwnArea(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); ArrangeSection(window);
        foreach (var form in vm.Forms)
        {
            vm.ChangeFormCommand.Execute(null); Check(); vm.SelectForm(form.Value); Check();
            foreach (var input in vm.Parameters.ToArray())
            {
                input.PendingText = input.Key switch { "b" => "120", "h" => "200", "d" or "D" => "80", "t" or "tw" => "5", "tf" => "12", "r" => "8", _ => "0" };
                // Full input-state combinations live in SectionDefinitionTransitionTests.
                if (Representative()) Check();
                input.Commit();
                if (Representative()) Check();
            }
            if (vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[0].Value;
            Check();
            if (!Representative()) continue;
            vm.EditNameCommand.Execute(null); vm.PendingName = "  " + new string('W', 80); Check();
            Assert.True(vm.CommitName()); Check();
            vm.ResetNameCommand.Execute(null); Check();
            var field = vm.Parameters[0]; var valid = field.PendingText;
            field.PendingText = "invalid"; Check(); field.Commit(); Check();
            field.PendingText = valid; field.Commit(); Check();
        }
        vm.ChangeFormCommand.Execute(null); vm.SelectForm(SectionEditorType.Manual);
        foreach (var configuration in vm.Configurations)
        { vm.Configuration = configuration; Check(); Fill(vm); Check(); }
        window.Close();

        bool Representative() => vm.Type is SectionEditorType.Circle or SectionEditorType.ISection or SectionEditorType.Manual;
        void Check()
        {
            foreach (var size in new[] { new Size(1000, 600), new Size(1100, 650), new Size(1250, 800), new Size(1000, 600), new Size(1100, 650) })
            {
                ArrangeSection(window, size.Width, size.Height);
                var grid = view.FindControl<Grid>("DefinitionColumns")!;
                var columns = grid.Children.OfType<Control>().Where(c => c.IsEffectivelyVisible).OrderBy(c => c.Bounds.Left).ToArray();
                for (int i = 1; i < columns.Length; i++)
                    Assert.True(columns[i].Bounds.Left >= columns[i - 1].Bounds.Right, $"{vm.Type}/{vm.LayoutState}: overlapping columns after resize");
                var cancel = view.FindControl<Button>("CancelButton")!;
                var footer = cancel.TranslatePoint(default, view)!.Value;
                Assert.InRange(footer.Y, 0, size.Height - cancel.Bounds.Height);
                var bodyStart = grid.TranslatePoint(default, view)!.Value;
                Assert.True(footer.Y + cancel.Bounds.Height <= bodyStart.Y, "Commands overlap definition body");
                foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.FindAncestorOfType<TextBox>() is null))
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, "Horizontal overflow after editing/resize");
            }
        }
    }
    [Theory]
    [InlineData("de-DE", 1000, 600)] [InlineData("en-US", 1000, 600)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public void CompactHeaderInputsAndPropertyBaselinesStayAlignedInBothUnitSystems(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        {
            var vm = new SectionDefinitionViewModel(new(units)); var view = new SectionDefinitionView { DataContext = vm };
            var window = new Window { Content = view }; window.Show(); ArrangeSection(window, width, height);
            Assert.False(view.FindControl<Button>("ChangeFormButton")!.IsEffectivelyVisible);
            foreach (var form in vm.Forms)
            {
                vm.ChangeFormCommand.Execute(null); vm.SelectForm(form.Value); ArrangeSection(window, width, height);
                AssertHeader(); Assert.True(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible); Assert.False(vm.CanSaveLibrary);
                AssertFields(view.FindControl<ItemsControl>("GeometryInputList")!);
                Fill(vm, false); ArrangeSection(window, width, height);
                AssertHeader(); Assert.True(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible);
                AssertProperties();
                // The shared error chrome is covered here for compact and multi-row forms;
                // all form/input/configuration states are exercised by Product tests.
                if (form.Value is SectionEditorType.Circle or SectionEditorType.ISection)
                {
                    var field = vm.Parameters[0]; var valid = field.PendingText;
                    field.PendingText = "pending"; ArrangeSection(window, width, height);
                    Assert.True(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible); Assert.False(vm.CanSaveLibrary);
                    field.PendingText = "invalid"; field.Commit(); ArrangeSection(window, width, height);
                    Assert.True(field.HasError); Assert.True(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible); Assert.False(vm.CanSaveLibrary);
                    AssertFields(view.FindControl<ItemsControl>("GeometryInputList")!);
                    field.PendingText = valid; field.Commit(); ArrangeSection(window, width, height);
                }
            }
            vm.SelectForm(SectionEditorType.Manual);
            foreach (var configuration in vm.Configurations)
            {
                vm.Configuration = configuration; Fill(vm, false); ArrangeSection(window, width, height);
                Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
                Assert.Equal(culture == "de-DE" ? "Querschnittswerte" : "Section properties",
                    view.FindControl<Grid>("ValuesColumn")!.Children.OfType<TextBlock>().Single().Text);
                Assert.Equal(2, view.FindControl<Grid>("DefinitionColumns")!.ColumnDefinitions.Count);
                var groups = view.FindControl<Grid>("ValuesColumn")!.GetVisualDescendants().OfType<TextBox>()
                    .Where(t => t.IsEffectivelyVisible && t.DataContext is SectionDefinitionInput).ToArray();
                Assert.Equal(vm.Parameters.Count, groups.Length);
                var x = groups[0].TranslatePoint(default, view)!.Value.X;
                foreach (var box in groups)
                {
                    Assert.Equal(88, box.Bounds.Width); Assert.InRange(Math.Abs(x - box.TranslatePoint(default, view)!.Value.X), 0, .5);
                    var row = Assert.IsType<Grid>(box.Parent);
                    var label = row.Children.OfType<Grid>().Single(); var unit = row.Children.OfType<TextBlock>().Single();
                    Assert.True(box.Bounds.Left - label.Bounds.Right >= 7.5);
                    Assert.InRange(unit.Bounds.Left - box.Bounds.Right, 7.5, 8.5);
                    Assert.Equal(Avalonia.Media.TextAlignment.Right, box.TextAlignment);
                    Assert.False(box.IsReadOnly);
                }
            }
            Assert.Equal(Strings.SectionDefinitionCustomName, vm.DisplayName);
            window.Close();

            void AssertHeader()
            {
                var header = view.FindControl<Grid>("DefinitionHeader")!;
                var icon = view.FindControl<SectionShapeIcon>("SelectedFormIcon")!;
                Assert.True(icon.IsEffectivelyVisible); Assert.Same(view.FindControl<Grid>("ProfileCardContent"), icon.Parent);
                Assert.Equal(formType(), icon.Type); Assert.Equal(new Size(32, 32), icon.Bounds.Size);
                Assert.Null(view.FindControl<TextBlock>("SelectedFormLabel"));
                var changeForm = view.FindControl<Button>("ChangeFormButton")!;
                Assert.True(changeForm.IsEffectivelyVisible);
                Assert.Same(vm.ChangeFormCommand, changeForm.Command);
                Assert.DoesNotContain(changeForm, view.FindControl<StackPanel>("HeaderActions")!.Children);
                Assert.Equal(70, view.FindControl<Border>("ProfileCard")!.MinHeight);
                Assert.True(header.Bounds.Width <= width - 48);
                var title = view.FindControl<TextBlock>("DefinitionTitle")!; var edit = view.FindControl<PathIcon>("TitleEditIcon")!;
                Assert.InRange(edit.Bounds.Left - title.Bounds.Right, 7.5, 8.5);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
                    t => t.Text is { } text && (text.StartsWith("1. ") || text.StartsWith("2. ") || text.StartsWith("3. ")));
                SectionEditorType formType() => vm.Type!.Value;
            }
            void AssertFields(ItemsControl list)
            {
                double? fieldX = null, unitX = null;
                double widestLabel = 0;
                foreach (var box in list.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible))
                {
                    var row = Assert.IsType<Grid>(box.Parent);
                    var label = row.Children.OfType<Grid>().Single();
                    var texts = new[] { label.Children.OfType<TextBlock>().Single(), row.Children.OfType<TextBlock>().Single() };
                    var input = Assert.IsType<SectionDefinitionInput>(box.DataContext);
                    Assert.Equal(input.DisplayLabel, TechnicalTextPresenter.GetContent(texts[0])!.PlainText); Assert.Equal(input.Unit.Symbol, texts[1].Text);
                    Assert.Equal(88, box.Bounds.Width); Assert.Equal((double)Application.Current!.FindResource("BodySize")!, texts[0].FontSize); Assert.Equal(texts[0].FontSize, box.FontSize);
                    Assert.Equal(Avalonia.Media.TextAlignment.Right, box.TextAlignment);
                    Assert.True(box.Bounds.Left - label.Bounds.Right >= 7.5);
                    Assert.InRange(texts[1].Bounds.Left - box.Bounds.Right, 7.5, 8.5);
                    Assert.True(texts[0].TextLayout.Width <= texts[0].Bounds.Width + 1, $"Clipped input label: {texts[0].Text}");
                    widestLabel = Math.Max(widestLabel, label.DesiredSize.Width);
                    double currentField = box.TranslatePoint(default, list)!.Value.X, currentUnit = texts[1].TranslatePoint(default, list)!.Value.X;
                    fieldX ??= currentField; unitX ??= currentUnit;
                    Assert.Equal(fieldX.Value, currentField); Assert.Equal(unitX.Value, currentUnit);
                }
                Assert.True(fieldX!.Value >= widestLabel + 7);
            }
            void AssertProperties()
            {
                var values = view.FindControl<Grid>("ValuesColumn")!;
                Assert.Equal(culture == "de-DE" ? "Berechnete Querschnittswerte" : "Calculated section properties",
                    values.Children.OfType<TextBlock>().Single().Text);
                var rows = values.GetVisualDescendants().OfType<SectionPropertyRow>().Where(r => r.IsEffectivelyVisible).ToArray();
                Assert.Equal(1 + 3 * vm.AxisValues.Count, rows.Length);
                double? valueLeft = null, valueRight = null;
                foreach (var row in rows)
                {
                    var texts = row.Children.OfType<TextBlock>().ToArray();
                    double Baseline(TextBlock text) => text.TranslatePoint(default, values)!.Value.Y + text.TextLayout.TextLines[0].Baseline;
                    if (texts[0].TextLayout.TextLines.Count == 1) Assert.InRange(Math.Abs(Baseline(texts[0]) - Baseline(texts[1])), 0, .01);
                    Assert.True(row.Bounds.Height >= 28);
                    foreach (var text in texts)
                    {
                        Assert.Equal((double)Application.Current!.FindResource("BodySize")!, text.FontSize); Assert.True(text.Bounds.Height >= text.TextLayout.Height);
                        Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1, $"Clipped property: {text.Text}");
                    }
                    Assert.Equal(Avalonia.Media.FontWeight.Normal, texts[0].FontWeight);
                    Assert.Equal(Avalonia.Media.FontWeight.SemiBold, texts[1].FontWeight);
                    var point = texts[1].TranslatePoint(default, values)!.Value;
                    valueLeft ??= point.X; valueRight ??= point.X + texts[1].Bounds.Width;
                    Assert.InRange(Math.Abs(valueLeft.Value - point.X), 0, .5);
                    Assert.InRange(Math.Abs(valueRight.Value - point.X - texts[1].Bounds.Width), 0, .5);
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", false)]
    [InlineData("de-DE", true)] [InlineData("en-US", true)]
    public void InlineErrorsExposeDescriptionsWithoutChangingFormWidth(string culture, bool manual)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(manual ? SectionEditorType.Manual : SectionEditorType.ISection);
        Fill(vm, false);
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view };
        window.Show(); ArrangeSection(window, 1100, 650);
        int closes = 0; vm.CloseRequested += _ => closes++;
        var grid = view.FindControl<Grid>("DefinitionColumns")!;
        Assert.True(grid.ColumnDefinitions[0].Width.IsAbsolute);
        double inputWidth = grid.ColumnDefinitions[0].ActualWidth;
        var input = Input(vm, manual ? "A" : "tw");
        var box = view.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, input));
        var row = Assert.IsType<Grid>(box.Parent);
        Assert.Empty(row.Children.OfType<SectionInputHint>());
        Assert.Null(box.InnerRightContent); Assert.DoesNotContain("error", box.Classes);
        var before = box.TranslatePoint(default, view)!.Value; var padding = box.Padding;
        var preview = vm.Preview;
        box.Focus(); box.Text = "invalid"; Dispatcher.UIThread.RunJobs();
        Assert.Same(preview, vm.Preview); Assert.False(input.HasError);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        ArrangeSection(window, 1100, 650);
        Assert.True(input.HasError); Assert.Contains("error", box.Classes);
        var errorIcon = Assert.IsType<PathIcon>(box.InnerRightContent);
        Assert.False(errorIcon.Focusable); Assert.False(errorIcon.IsHitTestVisible);
        Assert.Equal(input.Error, ToolTip.GetTip(box));
        Assert.Equal(input.Error, Avalonia.Automation.AutomationProperties.GetHelpText(box));
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Text == input.Error);
        Assert.Equal(padding, box.Padding); Assert.Equal(before, box.TranslatePoint(default, view)!.Value);
        Assert.Equal(88, box.Bounds.Width);
        Assert.True(new Rect(box.Bounds.Size).Contains(new Rect(errorIcon.TranslatePoint(default, box)!.Value, errorIcon.Bounds.Size)));
        foreach (var size in new[] { new Size(1000, 600), new Size(1250, 800), new Size(1100, 650) })
        {
            ArrangeSection(window, size.Width, size.Height);
            Assert.Equal(inputWidth, grid.ColumnDefinitions[0].ActualWidth);
        }
        Assert.True(box.IsFocused); Assert.Equal(0, closes);
        box.Focus(); box.Text = manual ? "7200" : "6"; Dispatcher.UIThread.RunJobs();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); ArrangeSection(window, 1100, 650);
        Assert.False(input.HasError); Assert.Null(box.InnerRightContent); Assert.DoesNotContain("error", box.Classes);
        Assert.Equal(inputWidth, grid.ColumnDefinitions[0].ActualWidth);
        if (!manual)
        {
            var radius = Input(vm, "r");
            var info = view.GetVisualDescendants().OfType<SectionInputHint>().Single(h => h.IsEffectivelyVisible && ReferenceEquals(h.DataContext, radius));
            Assert.Equal(Strings.SectionDefinitionRadiusHint, info.Message);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Text == info.Message);
            info.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(ToolTip.GetIsOpen(info));
            info.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs(); Assert.Null(info.Flyout); Assert.True(ToolTip.GetIsOpen(info));
            info.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(ToolTip.GetIsOpen(info)); Assert.Equal(0, closes);
            Commit(vm, "r", "9999"); ArrangeSection(window, 1100, 650);
            var radiusBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, radius));
            Assert.IsType<PathIcon>(radiusBox.InnerRightContent);
            Assert.Equal(Strings.SectionDefinitionRadiusError, ToolTip.GetTip(radiusBox));
            Assert.Equal(inputWidth, grid.ColumnDefinitions[0].ActualWidth);
        }
        window.Close();
    }
}

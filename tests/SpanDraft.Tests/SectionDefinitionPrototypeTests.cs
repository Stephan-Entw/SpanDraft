using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class SectionDefinitionPrototypeTests
{
    private static SectionDefinitionInput Input(SectionDefinitionViewModel vm,string key) => vm.Parameters.Single(p=>p.Key==key);
    private static void Commit(SectionDefinitionViewModel vm,string key,string value)
    { var input=Input(vm,key); input.PendingText=value; input.Commit(); }
    private static void Fill(SectionDefinitionViewModel vm)
    {
        foreach (var input in vm.Parameters.ToArray())
            Commit(vm,input.Key,input.Key switch
            { "b"=>"120", "h"=>"200", "d" or "D"=>"80", "t" or "tw"=>"5", "tf"=>"12", "r"=>"8", "A"=>"7200", _=>input.Key.StartsWith('I')?"8640000":"144000" });
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void EachShapeHasImmediatePreviewButOnlyConfirmedCompleteValuesProduceSection(SectionEditorType type)
    {
        using var culture=new UiCultureScope("en-US");
        var vm=new SectionDefinitionViewModel(); Assert.True(vm.IsChoosingForm); Assert.Null(vm.Preview);
        vm.SelectForm(type);
        Assert.False(vm.IsChoosingForm); Assert.NotNull(vm.Preview); Assert.Null(vm.Preview.Centroid);
        Assert.All(vm.Parameters,p=>{ Assert.Equal("",p.PendingText); Assert.Null(p.ConfirmedValue); Assert.False(p.HasError); });
        Assert.All(vm.Preview.Dimensions.Values,d=>Assert.False(d.IsConfirmed));
        Assert.Equal("—",vm.Area); Assert.False(vm.CanConfirm);
        Fill(vm);
        var section=Assert.IsAssignableFrom<IParametricSectionDefinition>(vm.Section);
        Assert.True(vm.CanConfirm); Assert.NotEqual("—",vm.Area); Assert.NotNull(vm.Preview!.Centroid);
        Assert.All(vm.Preview.Dimensions.Values,d=>Assert.True(d.IsConfirmed));
        Assert.Equal(section.GeometryProperties.Centroid.Y.Meters,vm.Preview.Centroid!.Value.X);
        Assert.Equal(section.GeometryProperties.PrincipalAxisAngleRadians,vm.Preview.PrincipalAngle);
        if (type is not SectionEditorType.Rectangle)
            Assert.Contains(vm.Preview.Contours.SelectMany(c=>c),s=>s.Center is not null);
        Assert.Equal(type==SectionEditorType.Angle?SectionAxisDesignation.U:SectionAxisDesignation.Y,vm.SelectedAxis);
        vm.SelectedAxis=vm.AvailableAxes[1].Value;
        Assert.Single(vm.AxisValues,a=>a.IsSelected);
        Assert.Equal(vm.SelectedAxis,vm.AxisValues.Single(a=>a.IsSelected).Axis);
        Assert.Same(section,vm.Section);
    }
    [Fact]
    public void TypingInvalidCommitsAndGeometryConflictsNeverReplaceTheLastValidPreview()
    {
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection);
        var original=vm.Preview;
        Input(vm,"h").PendingText="3"; Assert.Same(original,vm.Preview);
        Input(vm,"h").PendingText="30"; Assert.Same(original,vm.Preview);
        Input(vm,"h").PendingText="300"; Assert.Same(original,vm.Preview);
        Assert.True(Input(vm,"h").Commit());
        Assert.Equal(.3,vm.Preview!.Height); Assert.True(vm.Preview.Dimensions["h"].IsConfirmed);
        Assert.Equal("",Input(vm,"b").PendingText); Assert.Null(Input(vm,"b").ConfirmedValue);
        Commit(vm,"b","280"); Commit(vm,"tw","1");
        Assert.Equal(.001,vm.Preview!.Dimensions["tw"].Value);
        var good=vm.Preview;
        Commit(vm,"tw","invalid"); Assert.Same(good,vm.Preview); Assert.Null(vm.Section); Assert.True(Input(vm,"tw").HasError);
        Commit(vm,"tw","300"); Assert.Same(good,vm.Preview); Assert.Equal(.3,Input(vm,"tw").ConfirmedValue); Assert.True(Input(vm,"tw").HasError);
        Commit(vm,"tw","1"); Assert.False(Input(vm,"tw").HasError); Assert.NotSame(good,vm.Preview);
        Commit(vm,"tf","12"); Commit(vm,"r","0"); Assert.True(vm.CanConfirm);
        var section=vm.Section; Input(vm,"tw").PendingText="2";
        Assert.Same(section,vm.Section); Assert.False(vm.CanConfirm);
        Commit(vm,"tw","2"); Assert.True(vm.CanConfirm);
    }
    [Theory]
    [InlineData(SectionEditorType.ISection,"tw",.002)]
    [InlineData(SectionEditorType.ISection,"r",.2)]
    [InlineData(SectionEditorType.RectangularHollow,"t",.1)]
    [InlineData(SectionEditorType.CircularHollow,"t",.1)]
    [InlineData(SectionEditorType.Angle,"r",.2)]
    public void SecondaryConfirmedDimensionsCanExpandOnlyMissingDimensions(SectionEditorType type,string key,double value)
    {
        var preview=SectionPreviewGeometryResolver.Resolve(type,new Dictionary<string,double>{{key,value}});
        Assert.Equal(value,preview.Dimensions[key].Value); Assert.True(preview.Dimensions[key].IsConfirmed);
        Assert.All(preview.Contours.SelectMany(c=>c),s=>Assert.True(double.IsFinite(s.Start.X)&&double.IsFinite(s.Start.Y)));
    }
    [Theory]
    [InlineData(ManualAxisConfiguration.Y)] [InlineData(ManualAxisConfiguration.Z)]
    [InlineData(ManualAxisConfiguration.U)] [InlineData(ManualAxisConfiguration.V)]
    [InlineData(ManualAxisConfiguration.YZ)] [InlineData(ManualAxisConfiguration.UV)]
    public void ManualDefinitionsHaveOnlyRequiredAIWAndNoGeometry(ManualAxisConfiguration configuration)
    {
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Manual);
        vm.Configuration=vm.Configurations.Single(c=>c.Value==configuration);
        Assert.Null(vm.Preview); Assert.Empty(vm.GeometryInputs); Assert.False(vm.HasPreview);
        Assert.Equal(1+vm.AvailableAxes.Count*2,vm.ManualInputs.Count);
        Assert.Equal(UnitProfile.Default[QuantityKind.Area],Input(vm,"A").Unit);
        Fill(vm);
        var section=Assert.IsType<ManualSectionDefinition>(vm.Section);
        Assert.Equal(vm.AvailableAxes.Count,section.Axes.Count); Assert.True(vm.CanConfirm);
        Assert.All(section.Axes,a=>Assert.Equal(a.PositiveSectionModulus,a.NegativeSectionModulus));
        Assert.All(vm.Parameters.Where(p=>p.Key.StartsWith('W')),p=>Assert.DoesNotContain("+",p.Key));
        if (vm.AvailableAxes.Count>1) vm.SelectedAxis=vm.AvailableAxes[1].Value;
        Assert.Equal(vm.SelectedAxis,vm.AxisValues.Single(a=>a.IsSelected).Axis);
    }
    [Fact]
    public void ShapeDraftsAndManualAxisBuffersSurviveChangingForm()
    {
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Commit(vm,"b","60"); var b=Input(vm,"b"); b.PendingText="pending";
        vm.ChangeFormCommand.Execute(null); Assert.True(vm.IsChoosingForm); Assert.False(vm.CanConfirm);
        vm.SelectForm(SectionEditorType.Manual); vm.Configuration=vm.Configurations.Single(c=>c.Value==ManualAxisConfiguration.UV);
        Commit(vm,"IU","1000"); var u=Input(vm,"IU"); vm.SelectedAxis=SectionAxisDesignation.V;
        vm.Configuration=vm.Configurations[0]; vm.Configuration=vm.Configurations.Single(c=>c.Value==ManualAxisConfiguration.UV);
        Assert.Same(u,Input(vm,"IU")); Assert.Equal(SectionAxisDesignation.V,vm.SelectedAxis);
        vm.SelectForm(SectionEditorType.Rectangle); Assert.Same(b,Input(vm,"b")); Assert.Equal("pending",b.PendingText); Assert.Equal(.06,b.ConfirmedValue);
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void AutomaticNamesUseRealDimensionsAndInlineManualNamesRemainStable(string culture)
    {
        using var scope=new UiCultureScope(culture);
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Commit(vm,"b","60"); Assert.Equal(Strings.ShapeRectangle,vm.DisplayName);
        Commit(vm,"h","120"); Assert.Contains("60 × 120 mm",vm.DisplayName);
        vm.BeginNameEdit(); vm.PendingName=""; Assert.False(vm.CommitName()); Assert.True(vm.HasNameError);
        vm.CancelNameEdit(); Assert.Contains("60 × 120",vm.DisplayName);
        vm.BeginNameEdit(); vm.PendingName="  My profile  "; Assert.True(vm.CommitName()); Assert.Equal("My profile",vm.DisplayName);
        Commit(vm,"h","200"); Assert.Equal("My profile",vm.DisplayName);
        vm.SelectForm(SectionEditorType.Manual); Assert.Equal("My profile",vm.DisplayName);
        var manual=new SectionDefinitionViewModel(); manual.SelectForm(SectionEditorType.Manual); Assert.Equal(Strings.SectionDefinitionCustomName,manual.DisplayName);
    }
    [Fact]
    public void NonDefaultUnitsAndPresentationDoNotChangeSIValuesOrManualInputKinds()
    {
        using var culture=new UiCultureScope("en-US");
        var vm=new SectionDefinitionViewModel(new(UnitProfile.UnitedStates,PresentationMode.Detailed));
        vm.SelectForm(SectionEditorType.Rectangle); Commit(vm,"b","2"); Commit(vm,"h","4");
        Assert.Equal(.0508,Assert.IsType<RectangleSectionGeometry>(vm.Section).Width.Meters);
        Assert.Contains("2 × 4 in",vm.DisplayName);
        vm.SelectForm(SectionEditorType.Manual); Fill(vm);
        Assert.Equal(UnitCatalog.SquareInch,Input(vm,"A").Unit);
        Assert.Equal(UnitCatalog.InchToFourth,Input(vm,"IY").Unit);
        Assert.Equal(UnitCatalog.CubicInch,Input(vm,"WY").Unit);
    }
    private static ISectionDefinitionLibrary Library(MainWindowViewModel main) => (ISectionDefinitionLibrary)Activator.CreateInstance(
        typeof(MainWindow).Assembly.GetType("SpanDraft.Desktop.Sections.SectionDefinitionLibrary")!, [main])!;
    [Fact]
    public async Task LibrarySaveIsExplicitTransactionalAndErrorsPreserveEveryInput()
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
        SectionDefinitionResult? result=null; vm.CloseRequested+=r=>result=r;
        Assert.True(await vm.ConfirmAsync()); Assert.NotNull(result); Assert.Empty(files.Writes); Assert.Empty(main.UserSections.All);
        vm.SaveToLibrary=true; files.FailWritePath="sections";
        var text=vm.Parameters.Select(p=>p.PendingText).ToArray(); var preview=vm.Preview;
        result=null; Assert.False(await vm.ConfirmAsync()); Assert.Null(result); Assert.True(vm.HasLibraryError);
        Assert.Same(preview,vm.Preview); Assert.Equal(text,vm.Parameters.Select(p=>p.PendingText)); Assert.Empty(main.UserSections.All);
        files.FailWritePath=null; Assert.True(await vm.ConfirmAsync()); Assert.Single(main.UserSections.All); Assert.Single(files.Writes);
        var duplicate=new SectionDefinitionViewModel(library:Library(main)); duplicate.SelectForm(SectionEditorType.Rectangle); Fill(duplicate);
        duplicate.SaveToLibrary=true; Assert.False(duplicate.CanConfirm); Assert.Equal(Strings.LibraryNameConflict,duplicate.LibraryError);
        duplicate.BeginNameEdit(); duplicate.PendingName="Another"; duplicate.CommitName(); Assert.True(duplicate.CanConfirm);
        var bytes=files.Data["sections"]; files.Data["sections"]=[123];
        Assert.False(await duplicate.ConfirmAsync()); Assert.Equal(new byte[]{123},files.Data["sections"]); Assert.Single(main.UserSections.All);
        files.Data["sections"]=bytes;
    }
    [Fact]
    public async Task UnavailableLibraryAllowsOneOffResultButNeverOverwritesDamagedData()
    {
        var files=new Files(); files.Data["sections"]=[123];
        var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        Assert.True(vm.CanConfirm); vm.SaveToLibrary=true; Assert.False(vm.CanConfirm); Assert.True(vm.HasLibraryError);
        vm.SaveToLibrary=false; Assert.True(await vm.ConfirmAsync()); Assert.Empty(files.Writes); Assert.Equal(new byte[]{123},files.Data["sections"]);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StandaloneDefinitionPreservesAnExistingProjectRevisionDirtyFlagAndHistory(bool save)
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        main.Setup.ApplyCommand.Execute(null);
        Assert.NotNull(main.Editor); Assert.NotNull(main.Session);
        var editor=main.Editor; var session=main.Session;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text="1500"; editor.DimensionLength.Confirm();
        var revision=session.CurrentRevision; var dirty=session.IsDirty; var undo=session.UndoHistory.ToArray(); var redo=session.RedoHistory.ToArray();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Angle); Fill(vm); vm.SaveToLibrary=save;
        Assert.True(await vm.ConfirmAsync());
        Assert.Same(editor,main.Editor); Assert.Same(revision,session.CurrentRevision); Assert.Equal(dirty,session.IsDirty);
        Assert.Equal(undo,session.UndoHistory); Assert.Equal(redo,session.RedoHistory);
    }
    [Fact]
    public async Task CancelWithLibraryCheckboxCheckedDoesNotSaveOrReturnADefinition()
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm); vm.SaveToLibrary=true;
        int closed=0; vm.CloseRequested+=r=>{Assert.Null(r);closed++;}; vm.CancelCommand.Execute(null);
        Assert.Equal(1,closed); Assert.Empty(files.Writes); Assert.Empty(main.UserSections.All);
        Input(vm,"b").PendingText="not committed"; Assert.False(await vm.ConfirmAsync()); Assert.Empty(files.Writes);
    }
    [Theory]
    [InlineData(SectionEditorType.RectangularHollow,"0.0625")]
    [InlineData(SectionEditorType.ISection,"0.0546875")]
    [InlineData(SectionEditorType.USection,"0.09375")]
    [InlineData(SectionEditorType.TSection,"0.0546875")]
    [InlineData(SectionEditorType.Angle,"0.109375")]
    public void BoundaryRadiiUseExactCoreContoursAndInvalidRadiusKeepsThoseContours(SectionEditorType type,string radius)
    {
        using var culture=new UiCultureScope("en-US");
        // Exact binary lengths exercise the Core boundary contract without decimal-to-double near-boundary ambiguity.
        var profile=UnitProfile.Default.WithUnit(QuantityKind.SectionDimension,UnitCatalog.Meter);
        var vm=new SectionDefinitionViewModel(new(profile));vm.SelectForm(type);
        foreach(var input in vm.Parameters.ToArray()) Commit(vm,input.Key,input.Key switch
        { "b"=>"0.125", "h"=>"0.25", "t" or "tw"=>"0.015625", "tf"=>"0.03125", _=>"0" });
        Commit(vm,"r",radius);
        var section=Assert.IsAssignableFrom<IParametricSectionDefinition>(vm.Section); var preview=vm.Preview;
        Assert.Equal(section.Geometry.OuterContour.Segments.Count,preview!.Contours[0].Count);
        Assert.Equal(section.Geometry.OuterContour.Segments.OfType<SectionArc>().Count(),preview.Contours[0].Count(s=>s.Center is not null));
        Commit(vm,"r","300");Assert.Null(vm.Section);Assert.Same(preview,vm.Preview);Assert.Equal(Strings.SectionDefinitionRadiusError,Input(vm,"r").Error);
    }
    [Fact]
    public void RealFormButtonsAxesAndInlineNameKeyboardWorkWithoutClosingTheHost()
    {
        using var environment=new DesktopControlEnvironment();
        var vm=new SectionDefinitionViewModel();var view=new SectionDefinitionView{DataContext=vm};var window=new Window{Content=view};window.Show();Arrange(window);
        Assert.Equal(8,view.GetVisualDescendants().OfType<SectionShapeIcon>().Count());
        var angle=view.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,SectionEditorType.Angle));
        angle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Arrange(window);
        Assert.Equal(SectionEditorType.Angle,vm.Type);Assert.False(view.FindControl<StackPanel>("FormChooser")!.IsEffectivelyVisible);
        var axis=view.FindControl<StackPanel>("AxisOptions")!.Children.OfType<RadioButton>().Last();
        axis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(SectionAxisDesignation.V,vm.SelectedAxis);
        vm.EditNameCommand.Execute(null);Dispatcher.UIThread.RunJobs();
        var name=view.FindControl<TextBox>("NameInput")!;name.Text="Temporary";Dispatcher.UIThread.RunJobs();
        name.SelectionStart=3;name.SelectionEnd=3;vm.PendingName="Temporary profile";Dispatcher.UIThread.RunJobs();Assert.Equal(name.SelectionStart,name.SelectionEnd);
        name.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Assert.False(vm.IsEditingName);Assert.NotEqual("Temporary",vm.DisplayName);
        vm.EditNameCommand.Execute(null);Dispatcher.UIThread.RunJobs();name.Text="My angle";
        name.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Assert.Equal("My angle",vm.DisplayName);
        vm.ChangeFormCommand.Execute(null);Assert.True(vm.IsChoosingForm);
        view.FindControl<Button>("ManualFormButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Arrange(window);
        Assert.True(vm.IsManual);Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);window.Close();
    }
    private static void Arrange(Window window,double width=1250,double height=800)
    {
        window.Width=width; window.Height=height; window.ApplyTemplate();
        for (int pass=0;pass<3;pass++)
        {
            window.Measure(new Size(width,height)); window.Arrange(new Rect(0,0,width,height));
            var root=(Control)window.Content!;
            foreach(var child in root.GetVisualDescendants().OfType<Control>()) { child.InvalidateMeasure(); child.InvalidateArrange(); }
            root.InvalidateMeasure(); root.InvalidateArrange(); root.Measure(new Size(width,height)); root.Arrange(new Rect(0,0,width,height));
            Dispatcher.UIThread.RunJobs();
        }
    }
    [Fact]
    public void ProductTextBoxesCommitOnEnterAndLostFocusAndDoNotSubmitTheDialog()
    {
        using var environment=new DesktopControlEnvironment();
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        var view=new SectionDefinitionView{DataContext=vm}; var window=new Window{Content=view}; window.Show(); Arrange(window);
        var boxes=view.FindControl<ItemsControl>("GeometryInputList")!.GetVisualDescendants().OfType<TextBox>().ToArray();
        var b=boxes.Single(x=>((SectionDefinitionInput)x.DataContext!).Key=="b");
        var h=boxes.Single(x=>((SectionDefinitionInput)x.DataContext!).Key=="h");
        int closed=0; vm.CloseRequested+=_=>closed++;
        b.Focus(); b.Text="60"; var old=vm.Preview; Assert.Same(old,vm.Preview);
        b.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
        Assert.Equal(.06,Input(vm,"b").ConfirmedValue); Assert.NotSame(old,vm.Preview); Assert.Equal(0,closed);
        h.Focus(); h.Text="120"; var partial=vm.Preview; Assert.Same(partial,vm.Preview);
        view.FindControl<Button>("TitleButton")!.Focus(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(.12,Input(vm,"h").ConfirmedValue); Assert.True(vm.CanConfirm); Assert.Equal(0,closed);
        window.Close();
    }
    [Theory]
    [InlineData("de-DE",1250,800)] [InlineData("en-US",1250,800)]
    [InlineData("de-DE",1100,650)] [InlineData("en-US",1100,650)]
    public void ProductionControlsRenderRequestedStatesAndManualModeReflows(string culture,double width,double height)
    {
        using var environment=new DesktopControlEnvironment(); using var scope=new UiCultureScope(culture);
        var vm=new SectionDefinitionViewModel(); var window=new SectionDefinitionWindow(vm);
        var view=(SectionDefinitionView)window.Content!; var owner=new Window(); owner.Show();
        var pending=window.ShowDialog<SectionDefinitionResult?>(owner);
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
            Arrange(window,width,height);
            foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>().Where(s=>s.IsEffectivelyVisible&&s.FindAncestorOfType<TextBox>() is null))
                Assert.True(scroll.Extent.Width<=scroll.Viewport.Width+1,name+": horizontal overflow");
            foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.IsEffectivelyVisible&&t.Bounds.Width>0&&t.Text?.Length>0))
                Assert.True(text.Bounds.Height+1>=text.TextLayout.Height,name+": clipped text "+text.Text);
            var confirm=view.FindControl<Button>("ConfirmButton")!; var point=confirm.TranslatePoint(default,view)!.Value;
            Assert.InRange(point.Y,0,height-confirm.Bounds.Height); Assert.InRange(point.X,0,width-confirm.Bounds.Width);
            if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not {Length:>0} directory) return;
            Directory.CreateDirectory(directory); view.InvalidateVisual(); foreach(var visual in view.GetVisualDescendants()) visual.InvalidateVisual();
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)width,(int)height),new Vector(96,96)); bitmap.Render(view);
            bitmap.Save(Path.Combine(directory,$"section-definition-{culture}-{width}x{height}-{name}.png"),PngBitmapEncoderOptions.Default);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TestButtonOnlyAppearsInCreateAndDialogResultLeavesSetupAndProjectUnchanged(bool accept)
    {
        using var environment=new DesktopControlEnvironment();
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var setup=main.Setup; var originalSection=setup.SelectedSection; var originalAxis=setup.BendingAxis; var originalMaterial=setup.SelectedMaterial;
        var view=new ProjectSetupView{DataContext=setup}; var owner=new Window{Content=view}; owner.Show(); Arrange(owner);
        var button=view.FindControl<Button>("TestSectionDefinitionButton")!; Assert.True(button.IsEffectivelyVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dialog=Assert.IsType<SectionDefinitionWindow>(Assert.Single(owner.OwnedWindows));
        var vm=Assert.IsType<SectionDefinitionViewModel>(dialog.DataContext); Assert.True(vm.IsChoosingForm);
        Assert.Same(setup.ResultPresentation,vm.Presentation);
        if (accept) { vm.SelectForm(SectionEditorType.Rectangle); Fill(vm); Assert.True(await vm.ConfirmAsync()); }
        else vm.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs(); Assert.Empty(owner.OwnedWindows);
        Assert.Same(originalSection,setup.SelectedSection); Assert.Equal(originalAxis,setup.BendingAxis); Assert.Same(originalMaterial,setup.SelectedMaterial);
        Assert.Equal(ProjectSetupPage.Overview,setup.Page); Assert.Null(main.Editor); Assert.Empty(files.Writes);
        var state=State(); var edit=new ProjectSetupViewModel(ProjectSetupMode.Edit,state.Document.Section,state.Document.BendingAxis,state.Document.Material,_=>{},()=>{});
        view.DataContext=edit; Arrange(owner); Assert.False(button.IsEffectivelyVisible); owner.Close();
    }
    [Fact]
    public async Task SavingBlocksWindowCloseAndReturnsRealSectionOnlyAfterPublication()
    {
        using var environment=new DesktopControlEnvironment();
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var release=new TaskCompletionSource(); var reached=new TaskCompletionSource();
        files.BeforeWrite=async(_,_,_)=>{reached.SetResult();await release.Task;};
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Circle); Fill(vm); vm.SaveToLibrary=true;
        var owner=new Window(); owner.Show(); var dialog=new SectionDefinitionWindow(vm); var resultTask=dialog.ShowDialog<SectionDefinitionResult?>(owner);
        var pending=vm.ConfirmAsync(); await WaitForCheckpoint(reached.Task,pending);
        Assert.True(vm.IsBusy); Assert.False(vm.CanConfirm); dialog.Close(); Assert.False(resultTask.IsCompleted); Assert.Empty(main.UserSections.All);
        release.SetResult(); Assert.True(await WaitFor(pending)); var result=await WaitFor(resultTask);
        Assert.NotNull(result); Assert.IsType<CircleSectionGeometry>(result.Section); Assert.Single(main.UserSections.All); owner.Close();
    }
}

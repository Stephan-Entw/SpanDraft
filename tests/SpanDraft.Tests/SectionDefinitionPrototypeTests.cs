using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
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
    private static void Fill(SectionDefinitionViewModel vm, bool selectAxis = true)
    {
        foreach (var input in vm.Parameters.ToArray())
            Commit(vm,input.Key,input.Key switch
            { "b"=>"120", "h"=>"200", "d" or "D"=>"80", "t" or "tw"=>"5", "tf"=>"12", "r"=>"8", "A"=>"7200", _=>input.Key.StartsWith('I')?"8640000":"144000" });
        if (selectAxis && vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[0].Value;
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
        if (vm.RequiresAxisSelection)
        {
            vm.SelectedAxis=vm.AvailableAxes[1].Value;
            Assert.Single(vm.AxisValues,a=>a.IsSelected);
            Assert.Equal(vm.SelectedAxis,vm.AxisValues.Single(a=>a.IsSelected).Axis);
        }
        else { Assert.Null(vm.SelectedAxis); Assert.Single(vm.AxisValues); Assert.All(vm.AxisValues,a=>Assert.False(a.IsSelectable)); }
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
        Commit(vm,"tf","12"); Commit(vm,"r","0"); vm.SelectedAxis=SectionAxisDesignation.Y; Assert.True(vm.CanConfirm);
        var section=vm.Section; Input(vm,"tw").PendingText="2";
        Assert.Same(section,vm.Section); Assert.False(vm.CanConfirm);
        Commit(vm,"tw","2"); Assert.False(vm.CanConfirm); vm.SelectedAxis=SectionAxisDesignation.Y; Assert.True(vm.CanConfirm);
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
        Assert.Null(vm.SelectedAxis); Assert.All(vm.AxisValues,a=>Assert.False(a.IsSelectable));
    }
    [Fact]
    public void ShapeDraftsAndManualAxisBuffersSurviveChangingForm()
    {
        var vm=new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Commit(vm,"b","60"); var b=Input(vm,"b"); b.PendingText="pending";
        vm.ChangeFormCommand.Execute(null); Assert.True(vm.IsChoosingForm); Assert.False(vm.CanConfirm);
        vm.SelectForm(SectionEditorType.Manual); vm.Configuration=vm.Configurations.Single(c=>c.Value==ManualAxisConfiguration.UV);
        Fill(vm); Commit(vm,"IU","1000"); var u=Input(vm,"IU"); vm.SelectedAxis=SectionAxisDesignation.V;
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
        vm.SelectForm(SectionEditorType.Manual); Assert.Equal(Strings.SectionDefinitionCustomName,vm.DisplayName);
        Assert.False(vm.HasCustomName);
        vm.BeginNameEdit(); vm.PendingName="Manual profile"; Assert.True(vm.CommitName());
        vm.SelectForm(SectionEditorType.Rectangle); Assert.Equal("My profile",vm.DisplayName);
        Assert.Equal(.2,Input(vm,"h").ConfirmedValue);
        vm.ResetName(); Assert.False(vm.HasCustomName); Assert.Contains("60 × 200 mm",vm.DisplayName);
        vm.SelectForm(SectionEditorType.Manual); Assert.Equal("Manual profile",vm.DisplayName);
        vm.ResetName(); Assert.Equal(Strings.SectionDefinitionCustomName,vm.DisplayName);
        var manual=new SectionDefinitionViewModel(); manual.SelectForm(SectionEditorType.Manual); Assert.Equal(Strings.SectionDefinitionCustomName,manual.DisplayName);
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void EachFormRestoresItsOwnExplicitNameAndInputs(string culture)
    {
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel();
        var types = vm.Forms.Select(f => f.Value).Append(SectionEditorType.Manual).ToArray();
        var values = new Dictionary<SectionEditorType, double?[]>();
        foreach (var type in types)
        {
            vm.SelectForm(type); Fill(vm);
            vm.BeginNameEdit(); vm.PendingName = type == SectionEditorType.ISection ? "HEB 200" : "Custom " + type;
            Assert.True(vm.CommitName());
            var input = vm.Parameters[0]; input.PendingText = "300"; Assert.True(input.Commit());
            values[type] = vm.Parameters.Select(p => p.ConfirmedValue).ToArray();
        }
        foreach (var type in types.Reverse())
        {
            vm.SelectForm(type);
            Assert.Equal(type == SectionEditorType.ISection ? "HEB 200" : "Custom " + type, vm.DisplayName);
            Assert.True(vm.HasCustomName); Assert.Equal(values[type], vm.Parameters.Select(p => p.ConfirmedValue));
        }
        vm.ResetName();
        foreach (var type in types)
        {
            vm.SelectForm(type);
            Assert.Equal(type != SectionEditorType.Rectangle, vm.HasCustomName);
            vm.ResetName(); Assert.False(vm.HasCustomName);
            Assert.Equal(type == SectionEditorType.Manual, vm.DisplayName == Strings.SectionDefinitionCustomName);
        }
    }
    [Fact]
    public void FormSwitchCommitsNameOnOriginalDraftAndRejectsInvalidName()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection);
        vm.BeginNameEdit(); vm.PendingName = "HEB 200"; vm.SelectForm(SectionEditorType.Circle);
        Assert.False(vm.IsEditingName); Assert.False(vm.HasCustomName); Assert.Equal(Strings.ShapeCircle, vm.DisplayName);
        vm.SelectForm(SectionEditorType.ISection); Assert.Equal("HEB 200", vm.DisplayName);
        vm.BeginNameEdit(); vm.PendingName = " "; vm.SelectForm(SectionEditorType.Manual);
        Assert.Equal(SectionEditorType.ISection, vm.Type); Assert.True(vm.HasNameError);
        vm.CancelNameEdit(); vm.SelectForm(SectionEditorType.Manual); Assert.False(vm.HasCustomName);
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
        files.FailWritePath="sections";
        var text=vm.Parameters.Select(p=>p.PendingText).ToArray(); var preview=vm.Preview;
        result=null; Assert.False(await vm.SaveLibraryAsync()); Assert.Null(result); Assert.True(vm.HasLibraryError);
        Assert.Same(preview,vm.Preview); Assert.Equal(text,vm.Parameters.Select(p=>p.PendingText)); Assert.Empty(main.UserSections.All);
        files.FailWritePath=null; Assert.True(await vm.SaveLibraryAsync()); Assert.Null(result); Assert.Single(main.UserSections.All); Assert.Single(files.Writes);
        var duplicate=new SectionDefinitionViewModel(library:Library(main)); duplicate.SelectForm(SectionEditorType.Rectangle); Fill(duplicate);
        Assert.True(duplicate.CanConfirm); Assert.True(duplicate.CanSaveLibrary); Assert.Null(duplicate.LibraryError);
        Assert.False(await duplicate.SaveLibraryAsync()); Assert.Equal(Strings.LibraryNameConflict, duplicate.LibraryError);
        duplicate.BeginNameEdit(); duplicate.PendingName="Another"; duplicate.CommitName(); Assert.True(duplicate.CanConfirm);
        var bytes=files.Data["sections"]; files.Data["sections"]=[123];
        Assert.False(await duplicate.SaveLibraryAsync()); Assert.Equal(new byte[]{123},files.Data["sections"]); Assert.Single(main.UserSections.All);
        files.Data["sections"]=bytes;
    }
    [Fact]
    public async Task UnavailableLibraryAllowsOneOffResultButNeverOverwritesDamagedData()
    {
        var files=new Files(); files.Data["sections"]=[123];
        var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        Assert.True(vm.CanConfirm); Assert.True(vm.CanSaveLibrary); Assert.False(vm.HasLibraryError);
        Assert.False(await vm.SaveLibraryAsync()); Assert.True(vm.HasLibraryError);
        Assert.True(await vm.ConfirmAsync()); Assert.Empty(files.Writes); Assert.Equal(new byte[]{123},files.Data["sections"]);
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
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Angle); Fill(vm); if (save) Assert.True(await vm.SaveLibraryAsync());
        Assert.True(await vm.ConfirmAsync());
        Assert.Same(editor,main.Editor); Assert.Same(revision,session.CurrentRevision); Assert.Equal(dirty,session.IsDirty);
        Assert.Equal(undo,session.UndoHistory); Assert.Equal(redo,session.RedoHistory);
    }
    [Fact]
    public async Task CancelDoesNotSaveOrReturnADefinition()
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
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
        Assert.Equal(8,view.GetVisualDescendants().OfType<SectionShapeIcon>().Count(c=>c.IsEffectivelyVisible));
        var angle=view.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,SectionEditorType.Angle));
        angle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Arrange(window);
        Assert.Equal(SectionEditorType.Angle,vm.Type);Assert.False(view.FindControl<StackPanel>("FormChooser")!.IsEffectivelyVisible);
        Fill(vm, false); Arrange(window);
        var axis=view.GetVisualDescendants().OfType<SectionAxisBlock>().Last(c=>c.IsEffectivelyVisible);
        axis.Select();Assert.Equal(SectionAxisDesignation.V,vm.SelectedAxis);
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
        var root = (Control)window.Content!;
        for (int pass = 0; pass < 2; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            // The platform proxy does not send native resize callbacks. Supply the viewport
            // constraint normally, without invalidating any control's layout caches.
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
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
        window.Show(); Arrange(window, 1000, 600);
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
        double previousX = -1;
        foreach (string digit in new[] { "4", "5", "6" })
        {
            Type(digit); Assert.Equal(input.PendingText.Length, box.CaretIndex);
            var top = Caret();
            // The one-pixel caret is snapped and inset by Avalonia. Compare to
            // the actual glyph position with room for that raster alignment.
            Assert.InRange(Math.Abs(top.X - presenter.TextLayout.HitTestTextPosition(box.CaretIndex).X), 0, 2);
            Assert.InRange(Math.Abs(top.X - presenter.TextLayout.Width), 0, 2);
            Assert.True(top.X > previousX, "Appending a digit moved the caret backwards"); previousX = top.X;
            Assert.Same(preview, vm.Preview); Assert.Null(input.ConfirmedValue);
        }
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
        Type("7"); Assert.Equal("4576", input.PendingText); Assert.Equal(3, box.CaretIndex);
        var caret = Caret();
        var expected = presenter.TextLayout.HitTestTextPosition(box.CaretIndex);
        Assert.InRange(Math.Abs(caret.X - expected.X), 0, 2);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
        Arrange(window, 1000, 600); Assert.Equal("456", input.PendingText); Assert.Equal(2, box.CaretIndex);
        box.SelectAll(); Type("12"); Assert.Equal("12", input.PendingText); Assert.Equal(2, box.CaretIndex);
        Assert.InRange(Math.Abs(Caret().X - presenter.TextLayout.Width), 0, 2);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.NotNull(input.ConfirmedValue); Assert.Equal(2, box.CaretIndex);
        window.Close();

        void Type(string text)
        {
            box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
            Arrange(window, 1000, 600);
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
            Arrange(window,width,height);
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
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var setup=main.Setup; var original=setup.SelectedSection; var axis=setup.BendingAxis;
        var view=new ProjectSetupView{DataContext=setup}; var owner=new Window{Content=view}; owner.Show(); Arrange(owner,width,height);
        var decorations=owner.WindowDecorations; var position=owner.Position;
        main.Setup.ChangeSectionCommand.Execute(null); Arrange(owner,width,height);
        var preview=Assert.Single(view.GetVisualDescendants().OfType<SectionDefinitionView>());
        Assert.Same(view,owner.Content); Assert.Empty(owner.OwnedWindows);
        var vm=Assert.IsType<SectionDefinitionViewModel>(preview.DataContext); Assert.False(vm.IsChoosingForm);
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
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        var setup=new ProjectSetupView { DataContext=main.Setup }; var owner=new Window { Content=setup }; owner.Show(); Arrange(owner);
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
    [Fact]
    public async Task SavingBlocksWindowCloseAndReturnsRealSectionOnlyAfterPublication()
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); await main.InitializeLibrariesAsync();
        main.Setup.ShowSections(); var vm=main.Setup.Definition!; vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        var original=main.Setup.SelectedSection; var release=new TaskCompletionSource(); var reached=new TaskCompletionSource();
        files.BeforeWrite=async (_,_,_) => { reached.SetResult(); await release.Task; };
        var pending=vm.SaveLibraryAsync(); await WaitForCheckpoint(reached.Task,pending);
        try
        {
            Assert.False(await main.RequestCloseAsync()); Assert.False(await vm.ConfirmAsync());
            Assert.Same(original,main.Setup.SelectedSection); Assert.Empty(main.UserSections.All);
        }
        finally { release.SetResult(); }
        Assert.True(await WaitFor(pending)); Assert.True(await vm.ConfirmAsync());
        Assert.IsType<CircleSectionGeometry>(main.Setup.SelectedSection); Assert.Null(main.Session);
        Assert.Single(main.UserSections.All);
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void LayoutAndAcceptanceFollowCommittedSectionAndExplicitSelection(SectionEditorType type)
    {
        var vm = new SectionDefinitionViewModel();
        Assert.Equal(SectionDefinitionLayout.FormSelection, vm.LayoutState);
        Assert.False(vm.ShowPreview); Assert.False(vm.ShowValues); Assert.False(vm.ShowLibraryAction); Assert.False(vm.ShowConfirm); Assert.False(vm.CanConfirm);
        vm.SelectForm(type);
        Assert.Equal(SectionDefinitionLayout.ParametricDraft, vm.LayoutState);
        Assert.True(vm.ShowPreview); Assert.False(vm.ShowValues); Assert.False(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        Fill(vm, false);
        Assert.Equal(SectionDefinitionLayout.ParametricSection, vm.LayoutState);
        Assert.True(vm.ShowValues); Assert.True(vm.ShowLibraryAction); Assert.True(vm.ShowConfirm);
        Assert.Null(vm.SelectedAxis);
        Assert.Equal(!vm.RequiresAxisSelection, vm.CanConfirm);
        var section = vm.Section; var preview = vm.Preview;
        if (vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[1].Value;
        Assert.True(vm.CanConfirm); Assert.Same(section, vm.Section); Assert.Same(preview, vm.Preview);
        var input = vm.Parameters[0]; input.PendingText = "bad";
        Assert.Equal(SectionDefinitionLayout.ParametricSection, vm.LayoutState);
        Assert.Same(preview, vm.Preview); Assert.False(vm.CanConfirm);
        input.Commit();
        Assert.Equal(SectionDefinitionLayout.ParametricDraft, vm.LayoutState);
        Assert.False(vm.ShowValues); Assert.False(vm.ShowLibraryAction); Assert.True(vm.ShowConfirm); Assert.False(vm.CanConfirm);
        Assert.Same(preview, vm.Preview); Assert.Empty(vm.AxisValues);
        vm.ChangeFormCommand.Execute(null);
        Assert.Equal(SectionDefinitionLayout.FormSelection, vm.LayoutState); Assert.False(vm.ShowPreview);
    }
    [Fact]
    public void ChangedValuesResetSelectionButTypingNamesAndEquivalentCommitsDoNot()
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
        vm.SelectedAxis = SectionAxisDesignation.Z;
        var preview = vm.Preview; Input(vm, "h").PendingText = "200.0";
        Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis); Assert.Same(preview, vm.Preview); Assert.False(vm.CanConfirm);
        Input(vm, "h").Commit(); Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis); Assert.True(vm.CanConfirm);
        vm.BeginNameEdit(); vm.PendingName = "Named"; vm.CommitName(); Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis);
        Commit(vm, "h", "240"); Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm);
        vm.SelectedAxis = SectionAxisDesignation.Y;
        vm.ChangeFormCommand.Execute(null); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        vm.SelectForm(SectionEditorType.Rectangle); Assert.Equal(SectionAxisDesignation.Y, vm.SelectedAxis);
        Commit(vm, "h", "120"); Assert.True(vm.AxesEquivalent); Assert.True(vm.CanConfirm); Assert.Null(vm.SelectedAxis);
        Commit(vm, "h", "121"); Assert.True(vm.RequiresAxisSelection); Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm);
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    public async Task EquivalentParametricAxesHaveOneNeutralBlockAndDeterministicResult(SectionEditorType type)
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm, false);
        if (type is SectionEditorType.Rectangle or SectionEditorType.RectangularHollow) Commit(vm, "h", "120");
        Assert.True(vm.AxesEquivalent); Assert.False(vm.RequiresAxisSelection); Assert.True(vm.CanConfirm);
        var block = Assert.Single(vm.AxisValues);
        Assert.Contains("y-y / z-z", block.Label); Assert.Contains(Strings.SectionDefinitionEquivalent, block.Label);
        Assert.EndsWith(" I", block.InertiaLabel); Assert.EndsWith(" W+", block.PositiveLabel); Assert.EndsWith(" W−", block.NegativeLabel);
        Assert.False(block.IsSelectable); Assert.False(block.IsSelected); Assert.Null(vm.SelectedAxis);
        SectionDefinitionResult? result = null; vm.CloseRequested += r => result = r;
        Assert.True(await vm.ConfirmAsync()); Assert.Equal(SectionAxisDesignation.Y, result!.BendingAxis);
    }
    [Theory]
    [InlineData(ManualAxisConfiguration.YZ)] [InlineData(ManualAxisConfiguration.UV)]
    public void ManualPairBlocksStayEditableAndClassifyOnlyActualDifferences(ManualAxisConfiguration configuration)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Manual);
        vm.Configuration = vm.Configurations.Single(c => c.Value == configuration); Fill(vm, false);
        Assert.Equal(SectionDefinitionLayout.Manual, vm.LayoutState); Assert.False(vm.ShowPreview);
        Assert.True(vm.AxesEquivalent); Assert.True(vm.CanConfirm); Assert.Equal(2, vm.AxisValues.Count);
        var blocks = vm.AxisValues.ToArray();
        Assert.All(blocks, b => { Assert.Equal(2, b.Inputs.Count); Assert.False(b.IsSelectable); });
        string second = vm.AvailableAxes[1].Value.ToString();
        Commit(vm, "W" + second, "100000");
        Assert.True(vm.RequiresAxisSelection); Assert.False(vm.CanConfirm);
        Assert.All(vm.AxisValues, b => { Assert.True(b.IsSelectable); Assert.DoesNotContain(Strings.SectionDefinitionStrongAxis, b.Label); Assert.DoesNotContain(Strings.SectionDefinitionWeakAxis, b.Label); });
        vm.SelectedAxis = vm.AvailableAxes[1].Value; Assert.True(vm.CanConfirm);
        Commit(vm, "I" + second, "4320000"); Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm);
        Assert.Contains(Strings.SectionDefinitionStrongAxis, vm.AxisValues[0].Label);
        Assert.Contains(Strings.SectionDefinitionWeakAxis, vm.AxisValues[1].Label);
        Assert.Equal(blocks, vm.AxisValues); // No replacement of blocks or input buffers on commit.
        vm.SelectedAxis = vm.AvailableAxes[0].Value;
        Commit(vm, "A", "7300"); Assert.Null(vm.SelectedAxis);
    }
    [Theory]
    [InlineData(1e-300)] [InlineData(1e-20)] [InlineData(1)] [InlineData(1e20)]
    public void EquivalenceUsesRelativeRawSIValuesAndBothSignedModuli(double scale)
    {
        SectionAxisProperties Axis(SectionAxisDesignation axis, double i, double plus, double minus) => new(axis,
            SpanDraft.Core.Units.SecondMomentOfArea.FromMetersToTheFourth(i * scale),
            SpanDraft.Core.Units.SectionModulus.FromCubicMeters(plus * scale), SpanDraft.Core.Units.SectionModulus.FromCubicMeters(minus * scale));
        var first = Axis(SectionAxisDesignation.Y, 2, 3, 4);
        Assert.True(SectionDefinitionViewModel.HaveEquivalentProperties(first, Axis(SectionAxisDesignation.Z, 2 * (1 + 5e-10), 3, 4)));
        Assert.False(SectionDefinitionViewModel.HaveEquivalentProperties(first, Axis(SectionAxisDesignation.Z, 2 * (1 + 2e-9), 3, 4)));
        Assert.False(SectionDefinitionViewModel.HaveEquivalentProperties(first, Axis(SectionAxisDesignation.Z, 2, 3.1, 4)));
        Assert.False(SectionDefinitionViewModel.HaveEquivalentProperties(first, Axis(SectionAxisDesignation.Z, 2, 3, 4.1)));
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void EveryShapeAndManualInputHasLocalizedDescriptionAndSymbol(string culture)
    {
        using var scope = new UiCultureScope(culture); var vm = new SectionDefinitionViewModel();
        foreach (var form in vm.Forms)
        {
            vm.SelectForm(form.Value);
            Assert.All(vm.GeometryInputs, input => { Assert.EndsWith(" " + input.Symbol, input.DisplayLabel); Assert.True(input.DisplayLabel.Length > input.Symbol.Length + 1); Assert.Contains(input.Unit.Symbol, input.Label); });
            if (form.Value is SectionEditorType.Rectangle or SectionEditorType.RectangularHollow) Assert.Equal("h", vm.GeometryInputs[0].Key);
        }
        vm.SelectForm(SectionEditorType.ISection); Assert.Equal(Strings.SectionDefinitionShapeI, vm.SelectedFormName);
        vm.SelectForm(SectionEditorType.TSection); Assert.Equal(Strings.SectionDefinitionFlangeWidth + " b", Input(vm, "b").DisplayLabel);
        vm.SelectForm(SectionEditorType.RectangularHollow); Assert.Equal(Strings.SectionDefinitionOuterRadius + " r", Input(vm, "r").DisplayLabel);
        vm.SelectForm(SectionEditorType.Manual);
        Assert.Equal(Strings.SectionDefinitionArea + " A", Input(vm, "A").DisplayLabel);
        Assert.Equal(Strings.SectionDefinitionInertia + " Iy", Input(vm, "IY").DisplayLabel);
        Assert.Equal(Strings.SectionDefinitionModulus + " Wy", Input(vm, "WY").DisplayLabel);
    }
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1250, 800)]
    public void ProductControlsKeepParametricColumnsAndAxisBlocksSelectByMouseAndKeyboard(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); Arrange(window, width, height);
        var columns = view.FindControl<Grid>("DefinitionColumns")!;
        Assert.Single(columns.ColumnDefinitions);
        Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("CancelButton")!.IsEffectivelyVisible);
        Assert.Null(view.FindControl<StackPanel>("AxisOptions"));
        vm.SelectForm(SectionEditorType.ISection); Arrange(window, width, height);
        Assert.Equal(3, columns.ColumnDefinitions.Count);
        Assert.True(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        Assert.Equal(SectionEditorType.ISection, view.FindControl<SectionShapeIcon>("SelectedFormIcon")!.Type);
        Assert.Null(view.FindControl<TextBlock>("SelectedFormLabel"));
        Assert.Equal(vm.DisplayName, view.FindControl<TextBlock>("DefinitionTitle")!.Text);
        var canvas = view.FindControl<SectionPreviewControl>("SectionPreview")!;
        Assert.InRange(canvas.Bounds.Width, 180, width); Assert.InRange(canvas.Bounds.Height, 180, height);
        var referenceLayout = SectionPreviewDrawingLayout.Create(canvas.Geometry!, canvas.Bounds.Size,
            canvas.UnitProfile[QuantityKind.SectionDimension], text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
        Assert.Equal(new[] { SectionAxisDesignation.Y, SectionAxisDesignation.Z }, referenceLayout.Axes.Select(a => a.Axis));
        Assert.All(referenceLayout.Axes, a => Assert.True(a.IsReference)); Assert.Null(canvas.Geometry!.Centroid);
        Assert.Null(canvas.SelectedAxis);
        Fill(vm, false); Arrange(window, width, height);
        var realLayout = SectionPreviewDrawingLayout.Create(canvas.Geometry!, canvas.Bounds.Size,
            canvas.UnitProfile[QuantityKind.SectionDimension], text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
        Assert.All(realLayout.Axes, a => Assert.False(a.IsReference)); Assert.NotNull(canvas.Geometry!.Centroid);
        Assert.Equal(3, columns.ColumnDefinitions.Count);
        Assert.True(view.FindControl<TextBlock>("AxisSelectionHint")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled); Assert.Null(canvas.SelectedAxis);
        var blocks = view.FindControl<ItemsControl>("AxisValueList")!.GetVisualDescendants().OfType<SectionAxisBlock>().ToArray();
        Assert.Equal(2, blocks.Length);
        var pointer = new Pointer(71, PointerType.Mouse, true);
        var position = blocks[1].TranslatePoint(new Point(blocks[1].Bounds.Width - 3, blocks[1].Bounds.Height - 3), window)!.Value;
        blocks[1].RaiseEvent(new PointerPressedEventArgs(blocks[1], pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        blocks[1].RaiseEvent(new PointerReleasedEventArgs(blocks[1], pointer, window, position, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis); Assert.Equal(vm.SelectedAxis, canvas.SelectedAxis);
        blocks[0].Focus(); blocks[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        Assert.Equal(SectionAxisDesignation.Y, vm.SelectedAxis); Assert.True(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled);
        Assert.True(blocks[0].IsSelected); Assert.False(blocks[1].IsSelected);
        vm.ChangeFormCommand.Execute(null); Arrange(window, width, height);
        Assert.Single(columns.ColumnDefinitions); Assert.False(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        vm.SelectForm(SectionEditorType.Manual); Arrange(window, width, height);
        Assert.Equal(2, columns.ColumnDefinitions.Count); Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        window.Close();
    }
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1000, 600)] [InlineData("en-US", 1000, 600)]
    public void HiddenPropertiesLeaveSketchAtTheSameHorizontalPositionAndWidthAsThreeColumns(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); Arrange(window, width, height);
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
            vm.ChangeFormCommand.Execute(null); Arrange(window, width == 1100 ? 1250 : 1100, height == 650 ? 800 : 650);
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
            Arrange(window, width, height);
            Assert.Equal(3, grid.ColumnDefinitions.Count);
            Assert.True(grid.ColumnDefinitions[0].Width.IsAbsolute);
            Assert.Equal(grid.ColumnDefinitions[0].ActualWidth, inputs.Bounds.Width);
            Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 360);
            Assert.True(preview.Bounds.Width >= 180);
            Assert.InRange(canvas.Bounds.Width, 180, width);
        }
    }
    [Theory]
    [InlineData(SectionEditorType.RectangularHollow)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void DimensionEndpointsHaveActualThicknessAndRadiusAnchorsLieOnTrueArcs(SectionEditorType type)
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm, false);
        var geometry = vm.Preview!;
        foreach (var key in new[] { "t", "tw", "tf" }.Where(geometry.Dimensions.ContainsKey))
        {
            var dimension = geometry.Dimensions[key];
            Assert.Equal(dimension.Value, ((Vector)(dimension.End - dimension.Start)).Length, 12);
        }
        if (!geometry.Dimensions.TryGetValue("r", out var radius)) return;
        var arc = Assert.Single(geometry.Contours.SelectMany(c => c), s => s.Center is { } center && ((Vector)(center - radius.Start)).Length < 1e-12 && Math.Abs(((Vector)(s.Start - center)).Length - radius.Value) < 1e-12);
        Assert.Equal(radius.Value, ((Vector)(radius.End - arc.Center!.Value)).Length, 12);
        double start = Math.Atan2(arc.Start.Y - arc.Center.Value.Y, arc.Start.X - arc.Center.Value.X);
        Point midpoint = arc.Center.Value + new Vector(Math.Cos(start + arc.Sweep / 2), Math.Sin(start + arc.Sweep / 2)) * radius.Value;
        Assert.InRange(((Vector)(midpoint - radius.End)).Length, 0, 1e-12);
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle, "120", "2000")]
    [InlineData(SectionEditorType.Rectangle, "2000", "120")]
    [InlineData(SectionEditorType.ISection, "280", "300")]
    [InlineData(SectionEditorType.Angle, "120", "200")]
    public void DrawingLayoutExtendsAxesKeepsLabelsSeparateAndNeverWidensThinGeometry(SectionEditorType type, string width, string height)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm, false);
        Commit(vm, "b", width); Commit(vm, "h", height);
        if (type == SectionEditorType.ISection) Commit(vm, "tw", "0.1");
        var geometry = vm.Preview!;
        Size Measure(string text) => new(text.Length * 7, 16);
        var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter, Measure);
        Assert.Equal(geometry.Width / geometry.Height, layout.ProfileBounds.Width / layout.ProfileBounds.Height, 10);
        Assert.Equal(2, layout.Axes.Count);
        foreach (var axis in layout.Axes)
        {
            Assert.False(layout.ProfileBounds.Contains(axis.Start)); Assert.False(layout.ProfileBounds.Contains(axis.End));
            var center = layout.Project(geometry.Centroid!.Value);
            Vector direction = axis.End - axis.Start, offset = center - axis.Start;
            Assert.InRange(Math.Abs(direction.X * offset.Y - direction.Y * offset.X), 0, 1e-8);
        }
        foreach (var dimension in layout.Dimensions)
        {
            Assert.InRange(dimension.TextBounds.Left, 0, 420); Assert.InRange(dimension.TextBounds.Right, 0, 420);
            Assert.InRange(dimension.TextBounds.Top, 0, 440); Assert.InRange(dimension.TextBounds.Bottom, 0, 440);
            Assert.All(dimension.Arrows, a => Assert.Equal(1, a.TailDirection.Length, 10));
        }
        for (int i = 0; i < layout.Dimensions.Count; i++)
            for (int j = i + 1; j < layout.Dimensions.Count; j++) Assert.False(layout.Dimensions[i].TextBounds.Intersects(layout.Dimensions[j].TextBounds));
        if (type == SectionEditorType.ISection)
        {
            var tw = layout.Dimensions.Single(d => d.Key == "tw");
            Assert.Equal(2, tw.Arrows.Count);
            Assert.Equal(.0001 * layout.Scale, ((Vector)(tw.Arrows[1].Tip - tw.Arrows[0].Tip)).Length, 12);
            Assert.True(tw.Arrows[0].TailDirection.X < 0); Assert.True(tw.Arrows[1].TailDirection.X > 0);
        }
    }
    [Fact]
    public void PreviewDimensionsStaySymbolicAndR0DoesNotInventAnArc()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection);
        var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, new(520, 440), UnitCatalog.Millimeter, text => new(text.Length * 7, 16));
        Assert.All(layout.Dimensions, d => Assert.DoesNotContain("mm", d.Text));
        Assert.Equal("r", layout.Dimensions.Single(d => d.Key == "r").Text);
        Assert.Equal(2, layout.Axes.Count); Assert.All(layout.Axes, a => Assert.True(a.IsReference));
        Fill(vm, false); Commit(vm, "r", "0");
        Assert.All(vm.Preview!.Contours.SelectMany(c => c), s => Assert.Null(s.Center));
        layout = SectionPreviewDrawingLayout.Create(vm.Preview, new(520, 440), UnitCatalog.Millimeter, text => new(text.Length * 7, 16));
        Assert.Equal("r = 0 mm", layout.Dimensions.Single(d => d.Key == "r").Text);
    }
    [Fact]
    public void ManualBlockSelectionDoesNotStealEnterFromEditingOrReplaceFocusedInputs()
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Manual);
        vm.Configuration = vm.Configurations.Single(c => c.Value == ManualAxisConfiguration.YZ); Fill(vm, false);
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view }; window.Show(); Arrange(window);
        var list = view.FindControl<ItemsControl>("ManualInputList")!;
        Assert.All(list.GetVisualDescendants().OfType<SectionAxisBlock>(), b => { Assert.False(b.IsSelectable); Assert.False(b.Focusable); });
        Commit(vm, "WZ", "100000"); Arrange(window);
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
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view }; window.Show(); Arrange(window);
        var block = Assert.Single(view.FindControl<ItemsControl>("AxisValueList")!.GetVisualDescendants().OfType<SectionAxisBlock>());
        Assert.False(block.IsSelectable); Assert.False(block.Focusable); Assert.False(block.IsSelected);
        Assert.False(view.FindControl<TextBlock>("AxisSelectionHint")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled);
        block.Select(); Assert.Null(vm.SelectedAxis); Assert.Null(view.FindControl<SectionPreviewControl>("SectionPreview")!.SelectedAxis);
        var labels = block.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Contains(Strings.SectionDefinitionInertia + " I", labels); Assert.Contains(Strings.SectionDefinitionModulus + " W+", labels);
        Assert.Contains(Strings.SectionDefinitionModulus + " W−", labels);
        window.Close();
    }
    [Theory]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void LargeRadiiDoNotMoveThicknessDimensionsOntoFillets(SectionEditorType type)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(new(UnitProfile.Default.WithUnit(QuantityKind.SectionDimension, UnitCatalog.Meter))); vm.SelectForm(type);
        foreach (var input in vm.Parameters.ToArray()) Commit(vm, input.Key, input.Key switch
        { "b" => "0.125", "h" => "0.25", "tw" or "tf" or "t" => "0.015625", "r" => type == SectionEditorType.USection ? "0.09375" : type == SectionEditorType.Angle ? "0.109375" : "0.0546875", _ => "" });
        Assert.NotNull(vm.Section);
        var geometry = vm.Preview!; string key = type == SectionEditorType.Angle ? "t" : "tf";
        var dimension = geometry.Dimensions[key];
        Assert.Equal(geometry.Width, dimension.Start.X);
        Assert.Equal(geometry.Width, dimension.End.X);
        foreach (var point in new[] { dimension.Start, dimension.End })
            Assert.Contains(geometry.Contours.SelectMany(c => c), segment => segment.Center is null
                && Math.Abs(segment.Start.X - point.X) < 1e-12 && Math.Abs(segment.End.X - point.X) < 1e-12
                && point.Y >= Math.Min(segment.Start.Y, segment.End.Y) - 1e-12 && point.Y <= Math.Max(segment.Start.Y, segment.End.Y) + 1e-12);
        foreach (var size in new[] { new Size(250, 360), new Size(420, 440), new Size(520, 440) })
        {
            var layout = SectionPreviewDrawingLayout.Create(geometry, size, UnitCatalog.Meter,
                text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
            Assert.Equal(geometry.Dimensions.Count, layout.Dimensions.Count);
            Assert.Single(layout.Dimensions.Single(d => d.Key == "r").Lines);
            foreach (var drawing in layout.Dimensions.Where(d => d.Key is "t" or "tw" or "tf"))
            {
                if (drawing.Key == "tw") continue;
                Assert.False(LineIntersectsLabel(drawing.Lines[2], layout.ProfileBounds));
                Assert.All(drawing.Arrows, arrow => Assert.False(arrow.Bounds.Intersects(layout.ProfileBounds)));
            }
            foreach (var drawing in layout.Dimensions)
            {
                if (drawing.Key is not ("r" or "tw"))
                    Assert.False(drawing.TextBounds.Intersects(layout.ProfileBounds));
                foreach (var other in layout.Dimensions.Where(d => d.Key != drawing.Key))
                {
                    Assert.False(drawing.TextBounds.Inflate(2).Intersects(other.TextBounds));
                    Assert.All(other.Lines, line => Assert.False(LineIntersectsLabel(line, drawing.TextBounds)));
                }
            }
        }
    }
    [Theory]
    [InlineData("0.1")] [InlineData("0.0000001")]
    public void SubpixelWallsKeepTheirActualMeasureAndHaveTwoExtensionLines(string thickness)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection); Fill(vm, false);
        Commit(vm, "tw", thickness); Assert.NotNull(vm.Section);
        var geometry = vm.Preview!;
        var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter, text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
        var dimension = layout.Dimensions.Single(d => d.Key == "tw");
        Assert.Equal(2, dimension.Arrows.Count); Assert.True(dimension.Lines.Count >= 3);
        Assert.Equal(geometry.Dimensions["tw"].Value * layout.Scale, ((Vector)(dimension.Arrows[1].Tip - dimension.Arrows[0].Tip)).Length, 10);
        Assert.True(dimension.Arrows[0].TailDirection.X < 0); Assert.True(dimension.Arrows[1].TailDirection.X > 0);
    }
    [Theory]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public void LiveLayoutTransitionsKeepColumnsSeparatedWithoutForcedChildInvalidation(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); Arrange(window, width, height);
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
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void ConcaveRadiusLeaderPointsIntoTheOpeningAndNeverBacktracksThroughTheFlange(SectionEditorType type)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
        foreach (bool complete in new[] { false, true })
        {
            if (complete) Fill(vm, false);
            var geometry = vm.Preview!;
            var layout = SectionPreviewDrawingLayout.Create(geometry, new(520, 440), UnitCatalog.Millimeter,
                text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
            var radius = geometry.Dimensions["r"];
            var drawing = layout.Dimensions.Single(d => d.Key == "r");
            var arrow = Assert.Single(drawing.Arrows);
            Vector towardOpening = layout.Project(radius.Start) - layout.Project(radius.End);
            Assert.True(arrow.TailDirection.X * towardOpening.X + arrow.TailDirection.Y * towardOpening.Y > 0,
                $"{type}: radius leader enters material instead of the opening");
            Assert.All(drawing.Lines, line => Assert.True(line.End.X >= line.Start.X - .001, $"{type}: leader doubles back"));
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void AllAnnotationLabelsAndLinesStaySeparatedAtEveryCommitAndViewportSize(string culture)
    {
        using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        {
            var vm = new SectionDefinitionViewModel(new(units));
            foreach (var form in vm.Forms)
            {
                vm.SelectForm(form.Value); Check();
                foreach (var input in vm.Parameters.ToArray())
                {
                    Commit(vm, input.Key, input.Key switch { "b" => "120", "h" => "200", "d" or "D" => "80", "t" or "tw" => "5", "tf" => "12", "r" => "8", _ => "0" });
                    Check();
                }
                if (vm.Parameters.Any(p => p.Key == "r")) { Commit(vm, "r", "0"); Check(); }
                if (vm.Parameters.Any(p => p.Key == "h"))
                {
                    Commit(vm, "h", "2000"); Check();
                    Commit(vm, "b", "2000"); Commit(vm, "h", "120"); Check();
                }
            }
            void Check()
            {
                var geometry = vm.Preview!;
                foreach (var size in new[] { new Size(250, 360), new Size(250, 400), new Size(300, 320), new Size(380, 360), new Size(520, 440) })
                {
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                        text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
                    foreach (var dimension in layout.Dimensions.Where(d => d.Key is "t" or "tw" or "tf"))
                    {
                        Assert.All(dimension.Lines, line =>
                        {
                            Assert.True(new Rect(size).Contains(line.Start), $"{formName()}/{size}: clipped {dimension.Key} line start");
                            Assert.True(new Rect(size).Contains(line.End), $"{formName()}/{size}: clipped {dimension.Key} line end");
                        });
                        if (dimension.Key == "tw")
                        {
                            Assert.Equal(layout.ProfileBounds.Bottom - layout.ProfileBounds.Height * (geometry.Type == SectionEditorType.TSection ? .25 : .70),
                                dimension.Arrows[0].Tip.Y, 10);
                            continue;
                        }
                        Assert.False(LineIntersectsLabel(dimension.Lines[2], layout.ProfileBounds),
                            $"{formName()}/{size}: {dimension.Key} measure covers material");
                        Assert.All(dimension.Arrows, arrow =>
                        {
                            Assert.True(new Rect(size).Contains(arrow.Bounds), $"{formName()}/{size}: clipped {dimension.Key} arrow");
                            Assert.False(arrow.Bounds.Intersects(layout.ProfileBounds), $"{formName()}/{size}: {dimension.Key} filled arrow covers material");
                        });
                    }
                    var labels = layout.Dimensions.Select(d => (d.Key, d.TextBounds))
                        .Concat(layout.Axes.Select(a => (Key: a.Axis.ToString(), TextBounds: a.LabelBounds))).ToArray();
                    Assert.Equal(geometry.Dimensions.Keys.Order(), layout.Dimensions.Select(d => d.Key).Order());
                    for (int i = 0; i < labels.Length; i++)
                    {
                        var label = labels[i];
                        Assert.True(label.TextBounds.Left >= 0 && label.TextBounds.Right <= size.Width && label.TextBounds.Top >= 0 && label.TextBounds.Bottom <= size.Height,
                            $"{formName()}/{size}: {label.Key} outside viewport");
                        // Geometry-attached detail captions may use a concave profile's empty spaces.
                        if (label.Key is not ("r" or "tw"))
                            Assert.False(layout.ProfileBounds.Intersects(label.TextBounds), $"{formName()}/{size}: {label.Key} text covers profile");
                        for (int j = i + 1; j < labels.Length; j++) Assert.False(label.TextBounds.Inflate(2).Intersects(labels[j].TextBounds),
                            $"{formName()}/{size}/{units[QuantityKind.SectionDimension].Symbol}: {label.Key}/{labels[j].Key} texts overlap; profile={layout.ProfileBounds}; labels={string.Join("; ", labels.Select(l => l.Key + ":" + l.TextBounds))}");
                        foreach (var dimension in layout.Dimensions.Where(d => d.Key != label.Key))
                        {
                            foreach (var line in dimension.Lines)
                                Assert.False(LineIntersectsLabel(line, label.TextBounds), $"{formName()}/{size}/{units[QuantityKind.SectionDimension].Symbol}: {dimension.Key} line {line.Start}→{line.End} crosses {label.Key} text {label.TextBounds}; profile={layout.ProfileBounds}; labels={string.Join("; ", labels.Select(l => l.Key + ":" + l.TextBounds))}");
                            foreach (var arrow in dimension.Arrows)
                                Assert.False(arrow.Bounds.Intersects(label.TextBounds), $"{formName()}/{size}: {dimension.Key} arrow crosses {label.Key} text");
                        }
                    }
                }
                string formName() => vm.Type!.Value.ToString();
            }
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void RepeatedEditingAndResizingKeepsEveryFormCommandsAndColumnInItsOwnArea(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); Arrange(window);
        foreach (var form in vm.Forms)
        {
            vm.ChangeFormCommand.Execute(null); Check(); vm.SelectForm(form.Value); Check();
            foreach (var input in vm.Parameters.ToArray())
            {
                var previousPreview = vm.Preview;
                var previousState = vm.LayoutState;
                input.PendingText = input.Key switch { "b" => "120", "h" => "200", "d" or "D" => "80", "t" or "tw" => "5", "tf" => "12", "r" => "8", _ => "0" };
                Check(); Assert.Same(previousPreview, vm.Preview); Assert.Equal(previousState, vm.LayoutState);
                input.Commit(); Check();
            }
            if (vm.RequiresAxisSelection) { vm.SelectedAxis = vm.AvailableAxes[0].Value; Check(); }
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

        void Check()
        {
            foreach (var size in new[] { new Size(1000, 600), new Size(1100, 650), new Size(1250, 800), new Size(1000, 600), new Size(1100, 650) })
            {
                Arrange(window, size.Width, size.Height);
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
            var window = new Window { Content = view }; window.Show(); Arrange(window, width, height);
            Assert.False(view.FindControl<Button>("ChangeFormButton")!.IsEffectivelyVisible);
            foreach (var form in vm.Forms)
            {
                vm.ChangeFormCommand.Execute(null); vm.SelectForm(form.Value); Arrange(window, width, height);
                AssertHeader(); Assert.False(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible); Assert.False(vm.CanSaveLibrary);
                AssertFields(view.FindControl<ItemsControl>("GeometryInputList")!);
                Fill(vm, false); Arrange(window, width, height);
                AssertHeader(); Assert.True(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible);
                AssertProperties();
                var field = vm.Parameters[0]; var valid = field.PendingText;
                field.PendingText = "pending"; Arrange(window, width, height);
                Assert.False(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible); Assert.False(vm.CanSaveLibrary);
                field.PendingText = "invalid"; field.Commit(); Arrange(window, width, height);
                Assert.True(field.HasError); Assert.False(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible); Assert.False(vm.CanSaveLibrary);
                AssertFields(view.FindControl<ItemsControl>("GeometryInputList")!);
                field.PendingText = valid; field.Commit(); Arrange(window, width, height);
            }
            vm.SelectForm(SectionEditorType.Manual);
            foreach (var configuration in vm.Configurations)
            {
                vm.Configuration = configuration; Fill(vm, false); Arrange(window, width, height);
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
                    Assert.Equal(Avalonia.Media.TextAlignment.Left, box.TextAlignment);
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
                var title = view.FindControl<TextBlock>("DefinitionTitle")!; var edit = view.FindControl<SectionFluentIcon>("TitleEditIcon")!;
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
                    Assert.Equal(input.DisplayLabel, texts[0].Text); Assert.Equal(input.Unit.Symbol, texts[1].Text);
                    Assert.Equal(88, box.Bounds.Width); Assert.Equal(14, texts[0].FontSize); Assert.Equal(14, box.FontSize);
                    Assert.Equal(Avalonia.Media.TextAlignment.Left, box.TextAlignment);
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
                        Assert.Equal(14, text.FontSize); Assert.True(text.Bounds.Height >= text.TextLayout.Height);
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
    [Fact]
    public void FixedTWebMeasureKeepsConfiguredHeightWithoutMovingTheTrueCentroidAxis()
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.TSection); Fill(vm, false);
        Commit(vm, "r", "0"); Commit(vm, "tf", "1.7"); Assert.NotNull(vm.Section);
        var geometry = vm.Preview!;
        var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter,
            text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
        var axis = layout.Axes.Single(a => a.Axis == SectionAxisDesignation.Y);
        var initialMeasureY = layout.Project(geometry.Dimensions["tw"].Start).Y - 24;
        Assert.InRange(Math.Abs(initialMeasureY - axis.Start.Y), 0, 1);
        var web = layout.Dimensions.Single(d => d.Key == "tw");
        Assert.Equal(layout.ProfileBounds.Bottom - layout.ProfileBounds.Height * .25, web.Arrows[0].Tip.Y, 10);
        Assert.Equal(layout.Project(geometry.Centroid!.Value).Y, axis.Start.Y, 10);
        Assert.True(Math.Abs(web.Arrows[0].Tip.Y - axis.Start.Y) >= 6);
        Assert.Equal(3, web.Lines.Count);
    }
    [Theory]
    [InlineData("de-DE", SectionEditorType.ISection)] [InlineData("en-US", SectionEditorType.ISection)]
    [InlineData("de-DE", SectionEditorType.USection)] [InlineData("en-US", SectionEditorType.USection)]
    [InlineData("de-DE", SectionEditorType.TSection)] [InlineData("en-US", SectionEditorType.TSection)]
    public void WebMeasureStaysAtConfiguredHeightAndCaptionFollowsWebAcrossEditsUnitsAndViewportChanges(string culture, SectionEditorType type)
    {
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Check();
        Fill(vm, false); Check();
        Commit(vm, "tw", "0.0000001"); Check();
        Commit(vm, "h", "2000"); Check();
        Commit(vm, "b", "2000"); Commit(vm, "h", "120"); Check();

        void Check()
        {
            var geometry = vm.Preview!;
            foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
            foreach (var size in new[] { new Size(250, 360), new Size(380, 360), new Size(520, 440) })
            {
                var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                    text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
                var web = layout.Dimensions.Single(d => d.Key == "tw");
                double y = layout.ProfileBounds.Bottom - layout.ProfileBounds.Height * (type == SectionEditorType.TSection ? .25 : .70);
                Assert.All(web.Arrows, arrow => Assert.Equal(y, arrow.Tip.Y, 10));
                Assert.Equal(y, web.Lines[2].Start.Y, 10); Assert.Equal(y, web.Lines[2].End.Y, 10);
                Assert.Equal(y, web.TextBounds.Center.Y, 10);
                Assert.True(web.TextBounds.Left >= web.Arrows[1].Tip.X + 28 - 1e-10);
                Assert.Equal(web.TextBounds.Left - 4, web.Lines[2].End.X, 10);
                Assert.Equal(geometry.Dimensions["tw"].Value * layout.Scale,
                    ((Vector)(web.Arrows[1].Tip - web.Arrows[0].Tip)).Length, 10);
            }
        }
    }
    [Theory]
    [InlineData("de-DE", SectionEditorType.ISection)] [InlineData("en-US", SectionEditorType.ISection)]
    [InlineData("de-DE", SectionEditorType.USection)] [InlineData("en-US", SectionEditorType.USection)]
    [InlineData("de-DE", SectionEditorType.TSection)] [InlineData("en-US", SectionEditorType.TSection)]
    public void WebCaptionUsesScreenSpaceGapFromWebRatherThanFlangeOrViewportEdge(string culture, SectionEditorType type)
    {
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm, false);
        Commit(vm, "h", "400"); Commit(vm, "r", "0");
        foreach (string width in new[] { "80", "120", "240", "120" })
        {
            Commit(vm, "b", width);
            var geometry = vm.Preview!;
            foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
            foreach (var size in new[] { new Size(380, 360), new Size(520, 440) })
            {
                var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                    text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
                var web = layout.Dimensions.Single(d => d.Key == "tw");
                double preferredLeft = layout.Project(geometry.Dimensions["tw"].End).X + 28;
                if (type != SectionEditorType.TSection || Math.Abs(preferredLeft - web.TextBounds.Left) < 1e-10)
                    Assert.Equal(preferredLeft, web.TextBounds.Left, 10);
                else
                {
                    Assert.True(web.TextBounds.Left > preferredLeft);
                    var preferredCaption = new Rect(new Point(preferredLeft, web.TextBounds.Top), web.TextBounds.Size);
                    Assert.Contains(layout.Dimensions.Single(d => d.Key == "b").Lines.Take(2),
                        line => LineIntersectsLabel(line, preferredCaption.Inflate(2)));
                }
                Assert.Equal(web.TextBounds.Left - 4, web.Lines[2].End.X, 10);
                Assert.True(web.TextBounds.Top > layout.ProfileBounds.Top + geometry.Dimensions["tf"].Value * layout.Scale);
                Assert.True(web.TextBounds.Bottom < layout.ProfileBounds.Bottom - geometry.Dimensions["tf"].Value * layout.Scale);
                Assert.All(layout.Dimensions.Where(d => d.Key != "tw"), other =>
                    Assert.False(other.TextBounds.Inflate(2).Intersects(web.TextBounds)));
            }
        }
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void DirectDimensionsRetainExactEdgesAndUseUniformSlenderTriangleArrows(SectionEditorType type)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
        foreach (bool complete in new[] { false, true })
        {
            if (complete) Fill(vm, false);
            var geometry = vm.Preview!;
            var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter,
                text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
            Assert.Equal(geometry.Dimensions.Keys.Order(), layout.Dimensions.Select(d => d.Key).Order());
            foreach (var dimension in layout.Dimensions)
            {
                foreach (var arrow in dimension.Arrows)
                {
                    Assert.Equal(3, arrow.Points.Count);
                    Assert.Equal(4, ((Vector)(arrow.Points[2] - arrow.Points[1])).Length, 10);
                    var baseCenter = arrow.Points[1] + (arrow.Points[2] - arrow.Points[1]) / 2;
                    Assert.Equal(13.2, ((Vector)(baseCenter - arrow.Tip)).Length, 10);
                }
                if (dimension.Key == "r")
                {
                    var line = Assert.Single(dimension.Lines); var arrow = Assert.Single(dimension.Arrows);
                    Assert.Equal(layout.Project(geometry.Dimensions["r"].End), line.Start);
                    Assert.Equal(line.Start, arrow.Tip);
                    Assert.Equal(complete ? "r = 8 mm" : "r", dimension.Text);
                    Assert.True(line.Start.X != line.End.X && line.Start.Y != line.End.Y);
                }
                else
                {
                    Assert.Equal(2, dimension.Arrows.Count);
                    Assert.Equal(geometry.Dimensions[dimension.Key].Value * layout.Scale,
                        ((Vector)(dimension.Arrows[1].Tip - dimension.Arrows[0].Tip)).Length, 10);
                    Assert.Equal(3, dimension.Lines.Count);
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void RadiusCaptionsUseDimensionUnitsAndPreferredLeaderIsAttachedToGeometry(string culture)
    {
        using var scope = new UiCultureScope(culture);
        foreach (var type in new[] { SectionEditorType.RectangularHollow, SectionEditorType.ISection,
            SectionEditorType.USection, SectionEditorType.TSection, SectionEditorType.Angle })
        {
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
            foreach (int state in new[] { 0, 1, 2 })
            {
                if (state == 1) Fill(vm, false);
                if (state == 2) Commit(vm, "r", "0");
                var geometry = vm.Preview!; var radius = geometry.Dimensions["r"];
                foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
                foreach (var size in new[] { new Size(380, 360), new Size(520, 440) })
                {
                    var unit = units[QuantityKind.SectionDimension];
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, unit,
                        text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
                    var drawing = layout.Dimensions.Single(d => d.Key == "r");
                    var line = Assert.Single(drawing.Lines); var arrow = Assert.Single(drawing.Arrows);
                    Assert.Equal(radius.IsConfirmed ? "r = " + InputQuantityFormatter.WithUnit(radius.Value, unit) : "r", drawing.Text);
                    Assert.Equal(layout.Project(radius.End), line.Start); Assert.Equal(line.Start, arrow.Tip);
                    Vector ray = line.End - line.Start;
                    double angle = Math.Atan2(Math.Abs(ray.Y), ray.X) * 180 / Math.PI;
                    if (type != SectionEditorType.TSection || Math.Abs(angle - 35) < 1e-10 && Math.Abs(ray.Length - 32) < 1e-10)
                    {
                        Assert.Equal(32, ray.Length, 10);
                        Assert.Equal(35, angle, 10);
                    }
                    else
                    {
                        // The new long flange extensions can obstruct the preferred
                        // leader or caption. Keep the geometry attachment and avoid them.
                        Assert.InRange(ray.Length, 32 - 1e-10, 320 + 1e-10);
                        double radians = 35 * Math.PI / 180;
                        Point preferredEnd = line.Start + new Vector(Math.Cos(radians), Math.Sign(ray.Y) * Math.Sin(radians)) * 32;
                        var preferredCaption = new Rect(new Point(preferredEnd.X + 4, preferredEnd.Y - drawing.TextBounds.Height / 2), drawing.TextBounds.Size);
                        var preferredLeader = new PreviewDrawingLine(line.Start, preferredEnd);
                        Assert.Contains(layout.Dimensions.Single(d => d.Key == "b").Lines.Take(2), extension =>
                            LineIntersectsLabel(extension, preferredCaption.Inflate(2)) || LineIntersectsLabel(preferredLeader,
                                new Rect(new Point(extension.Start.X - .5, Math.Min(extension.Start.Y, extension.End.Y)),
                                    new Size(1, Math.Abs(extension.End.Y - extension.Start.Y)))));
                    }
                    Assert.Equal(line.End.X + 4, drawing.TextBounds.Left, 10);
                    Assert.Equal(line.End.Y, drawing.TextBounds.Center.Y, 10);
                    Assert.Equal(ray.X / ray.Length, arrow.TailDirection.X, 10);
                    Assert.Equal(ray.Y / ray.Length, arrow.TailDirection.Y, 10);
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void WidthDimensionsStayBelowProfilesAndTExtensionsReachActualFlangeCorners(string culture)
    {
        using var scope = new UiCultureScope(culture);
        foreach (var type in new[] { SectionEditorType.Rectangle, SectionEditorType.RectangularHollow,
            SectionEditorType.ISection, SectionEditorType.USection, SectionEditorType.TSection, SectionEditorType.Angle })
        {
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
            foreach (bool complete in new[] { false, true })
            {
                if (complete) Fill(vm, false);
                var geometry = vm.Preview!;
                foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
                foreach (var size in new[] { new Size(250, 360), new Size(380, 360), new Size(520, 440) })
                {
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                        text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
                    var width = layout.Dimensions.Single(d => d.Key == "b");
                    Assert.Equal(3, width.Lines.Count); Assert.Equal(2, width.Arrows.Count);
                    Assert.Equal(layout.ProfileBounds.Left, width.Arrows[0].Tip.X, 10);
                    Assert.Equal(layout.ProfileBounds.Right, width.Arrows[1].Tip.X, 10);
                    Assert.All(width.Arrows, arrow => Assert.Equal(layout.ProfileBounds.Bottom + 28, arrow.Tip.Y, 10));
                    Assert.True(width.TextBounds.Top > layout.ProfileBounds.Bottom);
                    var height = layout.Dimensions.Single(d => d.Key == "h");
                    Assert.Equal(layout.ProfileBounds.Bottom, height.Arrows[0].Tip.Y, 10);
                    Assert.Equal(layout.ProfileBounds.Top, height.Arrows[1].Tip.Y, 10);
                    Assert.Equal(layout.ProfileBounds.Left - 22, height.Arrows[0].Tip.X, 10);
                    Assert.Equal(layout.ProfileBounds.Left - 27, height.Lines[0].End.X, 10);
                    Assert.Equal(layout.ProfileBounds.Bottom, height.Lines[0].Start.Y, 10);
                    Assert.Equal(layout.ProfileBounds.Left - 5, height.Lines[1].Start.X, 10);
                    if (type == SectionEditorType.TSection)
                    {
                        // The lower height extension references the actual web, with the same
                        // five-DIP contour clearance used by the upper flange extension.
                        double webLeft = layout.Project(geometry.Dimensions["tw"].Start).X;
                        Assert.Equal(webLeft - 5, height.Lines[0].Start.X, 10);
                        double physicalWebLeft = (geometry.Width - geometry.Dimensions["tw"].Value) / 2;
                        Assert.Contains(geometry.Contours.SelectMany(c => c), segment =>
                            (Math.Abs(segment.Start.X - physicalWebLeft) < 1e-12 && Math.Abs(segment.Start.Y) < 1e-12)
                            || (Math.Abs(segment.End.X - physicalWebLeft) < 1e-12 && Math.Abs(segment.End.Y) < 1e-12));
                        double flangeBottom = geometry.Height - geometry.Dimensions["tf"].Value;
                        Assert.Equal(layout.Project(new Point(0, flangeBottom)) + new Vector(0, 5), width.Lines[0].Start);
                        Assert.Equal(layout.Project(new Point(geometry.Width, flangeBottom)) + new Vector(0, 5), width.Lines[1].Start);
                        foreach (var corner in new[] { new Point(0, flangeBottom), new Point(geometry.Width, flangeBottom) })
                            Assert.Contains(geometry.Contours.SelectMany(c => c), segment =>
                                ((Vector)(segment.Start - corner)).Length < 1e-12 || ((Vector)(segment.End - corner)).Length < 1e-12);
                        Assert.All(width.Lines.Take(2), line =>
                        {
                            Assert.Equal(flangeBottom * layout.Scale + 28, line.End.Y - line.Start.Y, 10);
                            Assert.Equal(layout.ProfileBounds.Bottom + 33, line.End.Y, 10);
                            Assert.True(line.End.Y > line.Start.Y);
                        });
                        Assert.Equal("b" + (complete ? " = " + InputQuantityFormatter.WithUnit(geometry.Width, units[QuantityKind.SectionDimension]) : ""), width.Text);
                    }
                    else
                    {
                        Assert.Equal(layout.ProfileBounds.Left - 5, height.Lines[0].Start.X, 10);
                    }
                }
            }
        }
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void ReferenceYZAxesExistImmediatelyWithoutInventingSectionPropertiesAndYieldToCoreAxes(SectionEditorType type)
    {
        foreach (string culture in new[] { "de-DE", "en-US" })
        {
            using var scope = new UiCultureScope(culture);
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Check(true);
            var preview = vm.Preview;
            vm.Parameters[0].PendingText = "200";
            Assert.Same(preview, vm.Preview); Check(true);
            if (vm.Parameters.Count > 1) { vm.Parameters[0].Commit(); Check(true); }
            Fill(vm, false); Check(false);

            void Check(bool reference)
            {
                var geometry = vm.Preview!;
                if (reference)
                {
                    Assert.Null(vm.Section); Assert.Null(geometry.Centroid); Assert.Null(geometry.PrincipalAngle);
                    Assert.Equal("—", vm.Area); Assert.False(vm.CanConfirm); Assert.Null(vm.SelectedAxis);
                }
                foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
                foreach (var size in new[] { new Size(250, 360), new Size(300, 320), new Size(520, 440) })
                {
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                        text => SchematicText.Measure(text, Avalonia.Media.Typeface.Default, 12));
                    Assert.Equal(2, layout.Axes.Count);
                    Assert.All(layout.Axes, axis => Assert.Equal(reference, axis.IsReference));
                    var center = reference ? layout.ProfileBounds.Center : layout.Project(geometry.Centroid!.Value);
                    foreach (var axis in layout.Axes)
                    {
                        Vector ray = axis.End - axis.Start, offset = center - axis.Start;
                        Assert.InRange(Math.Abs(ray.X * offset.Y - ray.Y * offset.X), 0, 1e-8);
                        Assert.False(layout.ProfileBounds.Contains(axis.Start)); Assert.False(layout.ProfileBounds.Contains(axis.End));
                        ray /= ray.Length;
                        double DistanceToEdge(Vector direction)
                        {
                            var bounds = layout.ProfileBounds;
                            double horizontal = Math.Abs(direction.X) < 1e-12 ? double.PositiveInfinity
                                : ((direction.X > 0 ? bounds.Right : bounds.Left) - center.X) / direction.X;
                            double vertical = Math.Abs(direction.Y) < 1e-12 ? double.PositiveInfinity
                                : ((direction.Y > 0 ? bounds.Bottom : bounds.Top) - center.Y) / direction.Y;
                            return Math.Min(horizontal, vertical);
                        }
                        Assert.Equal(6, ((Vector)(center - axis.Start)).Length - DistanceToEdge(-ray), 10);
                        Assert.Equal(20, ((Vector)(axis.End - center)).Length - DistanceToEdge(ray), 10);
                    }
                    if (reference)
                    {
                        Assert.Equal(new[] { SectionAxisDesignation.Y, SectionAxisDesignation.Z }, layout.Axes.Select(a => a.Axis));
                        var y = layout.Axes[0]; var z = layout.Axes[1];
                        Assert.Equal(center.Y, y.Start.Y, 10); Assert.Equal(center.Y, y.End.Y, 10);
                        Assert.Equal(center.X, z.Start.X, 10); Assert.Equal(center.X, z.End.X, 10);
                    }
                    else if (type == SectionEditorType.Angle)
                        Assert.Equal(new[] { SectionAxisDesignation.U, SectionAxisDesignation.V }, layout.Axes.Select(a => a.Axis));
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", false)]
    [InlineData("de-DE", true)] [InlineData("en-US", true)]
    public void FieldHintsExposeErrorsByFocusAndActivationWithoutChangingFormWidth(string culture, bool manual)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(manual ? SectionEditorType.Manual : SectionEditorType.ISection);
        Fill(vm, false);
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view };
        window.Show(); Arrange(window, 1100, 650);
        int closes = 0; vm.CloseRequested += _ => closes++;
        var grid = view.FindControl<Grid>("DefinitionColumns")!;
        Assert.True(grid.ColumnDefinitions[0].Width.IsAbsolute);
        double inputWidth = grid.ColumnDefinitions[0].ActualWidth;
        var input = Input(vm, manual ? "A" : "tw");
        var box = view.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, input));
        var row = Assert.IsType<Grid>(box.Parent);
        var errorHint = row.Children.OfType<SectionInputHint>().Single();
        Assert.False(errorHint.IsEffectivelyVisible); Assert.DoesNotContain("error", box.Classes);
        var before = box.TranslatePoint(default, view)!.Value; var padding = box.Padding;
        var preview = vm.Preview;
        box.Focus(); box.Text = "invalid"; Dispatcher.UIThread.RunJobs();
        Assert.Same(preview, vm.Preview); Assert.False(input.HasError);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Arrange(window, 1100, 650);
        Assert.True(input.HasError); Assert.True(errorHint.IsEffectivelyVisible); Assert.Contains("error", box.Classes);
        Assert.Equal(input.Error, errorHint.Message);
        Assert.Equal(input.Error, Assert.IsType<TextBlock>(ToolTip.GetTip(errorHint)).Text);
        Assert.Equal(input.Error, Avalonia.Automation.AutomationProperties.GetName(errorHint));
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Text == input.Error);
        Assert.Equal(padding, box.Padding); Assert.Equal(before, box.TranslatePoint(default, view)!.Value);
        Assert.Equal(88, box.Bounds.Width);
        Assert.True(box.Bounds.Contains(errorHint.Bounds));
        foreach (var size in new[] { new Size(1000, 600), new Size(1250, 800), new Size(1100, 650) })
        {
            Arrange(window, size.Width, size.Height);
            Assert.Equal(inputWidth, grid.ColumnDefinitions[0].ActualWidth);
        }
        errorHint.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(ToolTip.GetIsOpen(errorHint));
        var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        errorHint.RaiseEvent(escape); Assert.True(escape.Handled); Assert.False(ToolTip.GetIsOpen(errorHint)); Assert.Equal(0, closes);
        errorHint.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs(); Assert.Null(errorHint.Flyout); Assert.True(ToolTip.GetIsOpen(errorHint));
        errorHint.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(ToolTip.GetIsOpen(errorHint)); Assert.Equal(0, closes);
        box.Focus(); box.Text = manual ? "7200" : "6"; Dispatcher.UIThread.RunJobs();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Arrange(window, 1100, 650);
        Assert.False(input.HasError); Assert.False(errorHint.IsEffectivelyVisible); Assert.DoesNotContain("error", box.Classes);
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
            Commit(vm, "r", "9999"); Arrange(window, 1100, 650);
            var geometryHint = view.GetVisualDescendants().OfType<SectionInputHint>()
                .Single(h => h.IsEffectivelyVisible && h.Classes.Contains("errorHint") && ReferenceEquals(h.DataContext, radius));
            Assert.Equal(Strings.SectionDefinitionRadiusError, geometryHint.Message);
            Assert.Equal(inputWidth, grid.ColumnDefinitions[0].ActualWidth);
        }
        window.Close();
    }
    [Theory]
    [InlineData(false, null)] [InlineData(false, SectionAxisDesignation.Y)]
    [InlineData(true, null)] [InlineData(true, SectionAxisDesignation.Y)] [InlineData(true, SectionAxisDesignation.Z)]
    public void ProductPreviewAxisDashDotPatternKeepsScreenLengthsAndVisibleDotsAcrossSelection(bool complete, SectionAxisDesignation? selected)
    {
        using var environment = new DesktopControlEnvironment();
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection);
        if (complete) Fill(vm, false);
        var canvas = new SectionPreviewControl { Geometry = vm.Preview, SelectedAxis = selected };
        canvas.Measure(new Size(520, 440)); canvas.Arrange(new Rect(0, 0, 520, 440));
        var drawing = new Avalonia.Media.DrawingGroup();
        using (var context = drawing.Open()) canvas.Render(context);
        IEnumerable<Avalonia.Media.Drawing> Drawings(Avalonia.Media.Drawing item) => item is Avalonia.Media.DrawingGroup group
            ? group.Children.SelectMany(Drawings) : [item];
        var axes = Drawings(drawing).OfType<Avalonia.Media.GeometryDrawing>()
            .Where(d => d.Pen?.DashStyle is not null).ToArray();
        Assert.Equal(2, axes.Length);
        var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, canvas.Bounds.Size, canvas.UnitProfile[QuantityKind.SectionDimension],
            text =>
            {
                var formatted = SchematicText.Format(text, Avalonia.Media.Typeface.Default, 12, Avalonia.Media.Brushes.Gray);
                return new Size(formatted.Width, formatted.Height);
            });
        Rect LineBounds(Point start, Point end) => new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        bool SameBounds(Rect actual, Rect expected) => Math.Abs(actual.X - expected.X) < 1e-4
            && Math.Abs(actual.Y - expected.Y) < 1e-4 && Math.Abs(actual.Width - expected.Width) < 1e-4
            && Math.Abs(actual.Height - expected.Height) < 1e-4;
        foreach (var axis in layout.Axes)
        {
            Vector direction = axis.End - axis.Start; direction /= direction.Length;
            Point solidStart = axis.End - direction * 26;
            var solidBounds = LineBounds(solidStart, axis.End);
            var solid = Assert.Single(Drawings(drawing).OfType<Avalonia.Media.GeometryDrawing>(), d =>
                d.Brush is null && d.Pen is { DashStyle: null } && SameBounds(d.Geometry!.Bounds, solidBounds));
            Assert.Equal(!axis.IsReference && axis.Axis == selected ? 1.6 : .9, solid.Pen!.Thickness);
            Assert.Contains(axes, d => SameBounds(d.Geometry!.Bounds, LineBounds(axis.Start, solidStart)));
        }
        foreach (var axis in axes)
        {
            var pen = axis.Pen!;
            var dashes = Assert.IsAssignableFrom<IReadOnlyList<double>>(pen.DashStyle!.Dashes);
            Assert.Equal(Avalonia.Media.PenLineCap.Round, pen.LineCap);
            Assert.Equal(4, dashes.Count);
            Assert.All(dashes, dash => Assert.True(dash >= 0));
            Assert.Equal(20, dashes[0] * pen.Thickness + pen.Thickness, 10);
            Assert.Equal(4, dashes[1] * pen.Thickness - pen.Thickness, 10);
            Assert.Equal(1.6, dashes[2] * pen.Thickness + pen.Thickness, 10);
            Assert.Equal(4, dashes[3] * pen.Thickness - pen.Thickness, 10);
            Assert.Equal(29.6, dashes.Sum() * pen.Thickness, 10);
            if (!complete)
                Assert.Equal(.9, pen.Thickness); // Reference axes cannot be selected.
        }
        if (complete && selected.HasValue)
            Assert.Single(axes, axis => axis.Pen!.Thickness == 1.6);
    }
    private static bool LineIntersectsLabel(PreviewDrawingLine line, Rect rect)
    {
        // Clip the segment against the text box, without producing a bitmap.
        double low = 0, high = 1;
        Vector delta = line.End - line.Start;
        bool Clip(double direction, double distance)
        {
            if (Math.Abs(direction) < 1e-12) return distance >= 0;
            double fraction = distance / direction;
            if (direction < 0) low = Math.Max(low, fraction); else high = Math.Min(high, fraction);
            return low <= high;
        }
        return Clip(-delta.X, line.Start.X - rect.Left) && Clip(delta.X, rect.Right - line.Start.X)
            && Clip(-delta.Y, line.Start.Y - rect.Top) && Clip(delta.Y, rect.Bottom - line.Start.Y) && high > low;
    }
}

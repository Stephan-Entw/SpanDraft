using Avalonia;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Tests;

public sealed class SectionDefinitionPrototypeTests : SectionDefinitionPrototypeTestSupport
{
    private static ISectionDefinitionLibrary Library(MainWindowViewModel main) => (ISectionDefinitionLibrary)Activator.CreateInstance(
        typeof(MainWindow).Assembly.GetType("SpanDraft.Desktop.Sections.SectionDefinitionLibrary")!, [main])!;
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
        var vm=new SectionDefinitionViewModel(new(UnitProfile.UnitedStates,5));
        vm.SelectForm(SectionEditorType.Rectangle); Commit(vm,"b","2"); Commit(vm,"h","4");
        Assert.Equal(.0508,Assert.IsType<RectangleSectionGeometry>(vm.Section).Width.Meters);
        Assert.Contains("2 × 4 in",vm.DisplayName);
        vm.SelectForm(SectionEditorType.Manual); Fill(vm);
        Assert.Equal(UnitCatalog.SquareInch,Input(vm,"A").Unit);
        Assert.Equal(UnitCatalog.InchToFourth,Input(vm,"IY").Unit);
        Assert.Equal(UnitCatalog.CubicInch,Input(vm,"WY").Unit);
    }
    [Fact]
    public async Task LibrarySaveIsExplicitTransactionalAndErrorsPreserveEveryInput()
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
        SectionDefinitionResult? result=null; vm.CloseRequested+=r=>result=r;
        Assert.True(await vm.ConfirmAsync()); Assert.NotNull(result); Assert.Empty(files.Writes); Assert.Empty(main.UserSections.All);
        files.FailWritePath="sections";
        var text=vm.Parameters.Select(p=>p.PendingText).ToArray(); var preview=vm.Preview;
        result=null; Assert.False(await vm.SaveLibraryAsync()); Assert.Null(result); Assert.True(vm.HasLibraryError);
        Assert.Same(preview,vm.Preview); Assert.Equal(text,vm.Parameters.Select(p=>p.PendingText)); Assert.Empty(main.UserSections.All);
        files.FailWritePath=null; Assert.True(await vm.SaveLibraryAsync()); Assert.Null(result); Assert.Single(main.UserSections.All); Assert.Single(files.Writes);
        var duplicate=new SectionDefinitionViewModel(library:Library(main)); duplicate.SelectForm(SectionEditorType.Rectangle); Fill(duplicate);
        Assert.True(duplicate.CanConfirm); Assert.False(duplicate.CanSaveLibrary); Assert.True(duplicate.IsInLibrary); Assert.Null(duplicate.LibraryError);
        Assert.False(await duplicate.SaveLibraryAsync()); Assert.Null(duplicate.LibraryError);
        duplicate.BeginNameEdit(); duplicate.PendingName="Another"; duplicate.CommitName(); Assert.True(duplicate.CanConfirm);
        var bytes=files.Data["sections"]; files.Data["sections"]=[123];
        Assert.False(await duplicate.SaveLibraryAsync()); Assert.Equal(new byte[]{123},files.Data["sections"]); Assert.Single(main.UserSections.All);
        files.Data["sections"]=bytes;
    }
    [Fact]
    public async Task UnavailableLibraryAllowsOneOffResultButNeverOverwritesDamagedData()
    {
        var files=new Files(); files.Data["sections"]=[123];
        var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
        var vm=new SectionDefinitionViewModel(library:Library(main)); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        Assert.True(vm.CanConfirm); Assert.True(vm.CanSaveLibrary); Assert.False(vm.HasLibraryError);
        Assert.False(await vm.SaveLibraryAsync()); Assert.True(vm.HasLibraryError);
        Assert.True(await vm.ConfirmAsync()); Assert.Empty(files.Writes); Assert.Equal(new byte[]{123},files.Data["sections"]);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StandaloneDefinitionPreservesAnExistingProjectRevisionDirtyFlagAndHistory(bool save)
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
        ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
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
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
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
    public async Task SavingBlocksWindowCloseAndReturnsRealSectionOnlyAfterPublication()
    {
        var files=new Files(); var main=new MainWindowViewModel(sectionStore:new(files,"sections")); main.NewCommand.Execute(null); await main.InitializeLibrariesAsync();
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
        Assert.True(vm.ShowPreview); Assert.False(vm.ShowValues); Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
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
        Assert.False(vm.ShowValues); Assert.True(vm.ShowLibraryAction); Assert.True(vm.ShowConfirm); Assert.False(vm.CanConfirm);
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
        Size Measure(TechnicalText text) => new(text.PlainText.Length * 7, 16);
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
        var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, new(520, 440), UnitCatalog.Millimeter, text => new(text.PlainText.Length * 7, 16));
        Assert.All(layout.Dimensions, d => Assert.DoesNotContain("mm", d.Text));
        Assert.Equal("r", layout.Dimensions.Single(d => d.Key == "r").Text);
        Assert.Equal(2, layout.Axes.Count); Assert.All(layout.Axes, a => Assert.True(a.IsReference));
        Fill(vm, false); Commit(vm, "r", "0");
        Assert.All(vm.Preview!.Contours.SelectMany(c => c), s => Assert.Null(s.Center));
        layout = SectionPreviewDrawingLayout.Create(vm.Preview, new(520, 440), UnitCatalog.Millimeter, text => new(text.PlainText.Length * 7, 16));
        Assert.Equal("r = 0 mm", layout.Dimensions.Single(d => d.Key == "r").Text);
    }
}

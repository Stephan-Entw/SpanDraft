using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopSettingsInteractionTests
{
    public static IEnumerable<object[]> NonUnitDraftCases() =>
        from target in new[] { "support", "force", "moment", "distributed", "length" }
        from preference in new[] { "language", "region", "mode" }
        select new object[] { target, preference };

    [Theory]
    [MemberData(nameof(NonUnitDraftCases))]
    public async Task LocaleAndModeSavesPreserveActiveEditorDrafts(string target, string preference)
    {
        var files = new Files(); var main = CreateMain(files); var editor = main.Editor!;
        Edit(editor, target);
        var support = editor.SupportDraft; var point = editor.LoadDraft; var distributed = editor.DistributedLoadDraft;
        string text = editor.DimensionLength.Text; var revision = editor.Session.CurrentRevision;
        var next = preference switch
        {
            "language" => main.Settings with { UiLanguage = UiLanguagePreference.English },
            "region" => main.Settings with { RegionalFormat = RegionalFormatPreference.GermanGermany },
            _ => main.Settings with { SignificantDigits = 5 }
        };
        Assert.Null(await main.ApplySettingsAsync(SettingsPatch.From(next)));
        Assert.True(next.ContentEquals(SettingsCodec.Deserialize(files.Data["settings"])));
        Assert.Same(support, editor.SupportDraft); Assert.Same(point, editor.LoadDraft); Assert.Same(distributed, editor.DistributedLoadDraft);
        Assert.Equal(text, editor.DimensionLength.Text); Assert.Same(revision, editor.Session.CurrentRevision);
        Assert.True(UserSettings.SameUnits(UnitProfile.Default, main.ResultPresentation.Profile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LocaleAndModeSavesPreserveSetupDraftsWhileChangedInputUnitsRemainBlocked(bool section)
    {
        var files = new Files();
        var main = new MainWindowViewModel(files: files, services: TestServices.Create(settingsStore: new(files, "settings")));
        main.NewCommand.Execute(null);
        SectionDefinitionInput? input = null;
        if (section)
        {
            main.Setup.ShowSections(); main.Setup.Definition!.SelectForm(SectionEditorType.Rectangle);
            input = main.Setup.Definition.Parameters.First(); input.PendingText = "unfinished";
        }
        else { main.Setup.LengthInput.Begin(); main.Setup.LengthInput.Text = "unfinished"; }
        var next = main.Settings with { UiLanguage = UiLanguagePreference.English,
            RegionalFormat = RegionalFormatPreference.GermanGermany, SignificantDigits = 5 };
        Assert.Null(await main.ApplySettingsAsync(SettingsPatch.From(next)));
        Assert.Equal("unfinished", input?.PendingText ?? main.Setup.LengthInput.Text);
        Assert.NotNull(await main.ApplySettingsAsync(SettingsPatch.From(next.Select(UnitProfileKind.UnitedStates))));
        Assert.True(next.ContentEquals(main.Settings));
        Assert.Equal("unfinished", input?.PendingText ?? main.Setup.LengthInput.Text);
    }

    [Fact]
    public async Task MaterialEditingKeepsExactBuffersWhilePrecisionChangesAndRejectsAffectedUnitChanges()
    {
        var files = new Files(); var main = CreateMain(files);
        main.EditProject(); main.Setup.ShowMaterials();
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        selection.EditSelectedCommand.Execute(null);
        var editor = Assert.IsType<MaterialEditorViewModel>(selection.Editor);
        editor.Inputs[0].Text = "unfinished";
        var settings = main.Settings with { SignificantDigits = 4, Notation = EngineeringNotation.Off };
        Assert.Null(await main.ApplySettingsAsync(SettingsPatch.From(settings)));
        Assert.Equal("unfinished", editor.Inputs[0].Text);
        var writes = files.Writes.ToArray();
        Assert.NotNull(await main.ApplySettingsAsync(SettingsPatch.From(settings.WithUnit(QuantityKind.YoungsModulus, UnitCatalog.Ksi))));
        Assert.Equal(writes, files.Writes);
        Assert.Same(editor, selection.Editor); Assert.Equal("unfinished", editor.Inputs[0].Text);
        Assert.Same(UnitCatalog.Gigapascal, editor.Inputs[0].Unit);
    }

    [Fact]
    public async Task TheSettingsFreezeStopsNewOperationsInEveryWorkspaceOnlyWhileTheCommitRunsAndIsReleasedAfterwards()
    {
        var files = new Files(); var services = TestServices.Create(settingsStore: new(files, "settings"));
        var applying = CreateMain(files, services); var editing = CreateMain(files, services);
        var setup = new MainWindowViewModel(files: files, services: services); setup.NewCommand.Execute(null);
        var editor = editing.Editor!;
        var activeEditor = applying.Editor!;
        var editorBusyChanges = new List<bool>();
        editor.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(EditorViewModel.IsBusy)) editorBusyChanges.Add(editor.IsBusy);
        };
        Assert.True(activeEditor.EditLoad(LoadId));
        var activeDraft = activeEditor.LoadDraft!;
        activeDraft.ValueText = "-125";
        Assert.True(activeEditor.BeginLoadDrag(LoadId));
        var dragPreview = activeEditor.LoadPreview;
        setup.Setup.LengthInput.Begin();
        setup.Setup.LengthInput.Text = "1234";
        var committing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeWrite = async (path, _, _) => { if (path != "settings") return; committing.TrySetResult(); await release.Task; };

        // An open settings dialog alone locks nothing: the other workspaces stay usable.
        applying.SetSettingsDialogOpen(true);
        Assert.False(editing.IsWorkspaceBusy); Assert.True(editing.CanSaveProject); Assert.False(editor.PreserveDrafts);
        Assert.False(setup.IsWorkspaceBusy); Assert.True(setup.Setup.AddSegmentCommand.CanExecute(null));

        var revision = editor.Session.CurrentRevision;
        var longer = revision.State with { Document = editor.Document with { Length = M(2) } };
        var momentId = editor.Document.Loads[1].Id;
        var next = applying.Settings.WithUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter) with { SignificantDigits = 5 };
        var apply = applying.ApplySettingsAsync(SettingsPatch.From(next));
        var entering = await Task.WhenAny(committing.Task, apply);
        Assert.Same(committing.Task, entering);
        try
        {
            foreach (var workspace in new[] { applying, editing, setup })
            {
                Assert.True(workspace.IsBusy); Assert.True(workspace.IsWorkspaceBusy);
                Assert.False(workspace.CanNewProject); Assert.False(workspace.CanOpenProject);
                Assert.False(workspace.CanSaveProject); Assert.False(workspace.CanReport);
                Assert.False(workspace.SetResultPresentation(UnitProfile.StructuralEngineering, 3));
            }
            var writes = files.Writes.ToArray();
            Assert.False(await editing.NewAsync()); Assert.False(await editing.OpenAsync()); Assert.False(await editing.SaveAsync(saveAs: true));
            Assert.False(await editing.CloseProjectAsync()); Assert.False(await editing.ReportAsync());
            Assert.True(editor.PreserveDrafts); Assert.True(editor.Session.IsBusy);
            Assert.False(editor.Session.Commit(longer));
            Assert.False(setup.Setup.AddSegmentCommand.CanExecute(null)); Assert.True(setup.Setup.IsBusy);
            Assert.Same(revision, editor.Session.CurrentRevision); Assert.Equal(writes, files.Writes.ToArray());

            editor.MomentToolCommand.Execute(null);
            Assert.Null(editor.LoadTool);
            Assert.False(editor.EditLoad(momentId));
            Assert.False(editor.BeginLoadDrag(momentId));
            editor.DimensionLength.Begin();
            Assert.False(editor.DimensionLength.IsEditing);

            activeDraft.ValueText = "-250";
            activeEditor.UpdateLoadDrag(M(.5));
            Assert.False(activeEditor.EndLoadDrag());
            Assert.False(activeEditor.ConfirmLoad());
            activeEditor.CancelEditorInteraction();
            Assert.Equal(LoadInteraction.Drag, activeEditor.LoadState);
            Assert.Same(dragPreview, activeEditor.LoadPreview);
            Assert.Same(activeDraft, activeEditor.LoadDraft);
            Assert.Equal("-125", activeDraft.ValueText);

            setup.Setup.LengthInput.Text = "2000";
            Assert.False(setup.Setup.LengthInput.Confirm());
            Assert.True(setup.Setup.LengthInput.IsEditing);
            Assert.Equal("1234", setup.Setup.LengthInput.Text);
        }
        finally { release.TrySetResult(); }
        Assert.Null(await apply);
        foreach (var workspace in new[] { applying, editing, setup }) { Assert.False(workspace.IsBusy); Assert.False(workspace.IsWorkspaceBusy); }
        Assert.True(editing.CanSaveProject); Assert.False(editor.PreserveDrafts); Assert.False(editor.Session.IsBusy);
        Assert.True(setup.Setup.AddSegmentCommand.CanExecute(null));
        Assert.Equal(5, editing.Settings.SignificantDigits);
        Assert.Equal(new[] { true, false }, editorBusyChanges);
        Assert.True(editing.CanNewProject); Assert.True(editor.Session.Commit(longer));
        Assert.True(editor.EditLoad(momentId));
        Assert.Equal(UnitCatalog.KilonewtonMeter.Symbol, editor.LoadDraft!.Unit);
        Assert.Same(activeDraft, activeEditor.LoadDraft);
        Assert.Equal("-125", activeDraft.ValueText);
        Assert.True(activeEditor.EndLoadDrag());
        Assert.True(activeEditor.ConfirmLoad());
        Assert.Equal("1234", setup.Setup.LengthInput.Text);
        Assert.True(setup.Setup.LengthInput.Confirm());
    }

    [Fact]
    public async Task AnOpenSectionDefinitionPreservesPendingInputsUntilTheSettingsFreezeIsReleased()
    {
        var files = new Files();
        var services = TestServices.Create(settingsStore: new(files, "settings"));
        var applying = CreateMain(files, services);
        var setup = new MainWindowViewModel(files: files, services: services);
        setup.NewCommand.Execute(null);
        setup.Setup.ShowSections();
        var definition = setup.Setup.Definition!;
        definition.SelectForm(SectionEditorType.Circle);
        var diameter = Assert.Single(definition.Parameters);
        diameter.PendingText = "100";
        Assert.True(diameter.Commit());
        var confirmed = diameter.ConfirmedValue;
        var section = definition.Section;
        diameter.PendingText = "120";

        var persisting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeWrite = async (path, _, _) =>
        {
            if (path != "settings") return;
            persisting.TrySetResult();
            await release.Task;
        };
        var apply = applying.ApplySettingsAsync(SettingsPatch.From(applying.Settings with { SignificantDigits = 5 }));
        Assert.Same(persisting.Task, await Task.WhenAny(persisting.Task, apply));
        try
        {
            Assert.True(definition.IsBusy);
            Assert.False(definition.ChangeFormCommand.CanExecute(null));
            definition.SelectForm(SectionEditorType.Rectangle);
            diameter.PendingText = "150";
            Assert.False(diameter.Commit());
            Assert.False(await definition.ConfirmAsync());
            Assert.Same(definition, setup.Setup.Definition);
            Assert.Equal(SectionEditorType.Circle, definition.Type);
            Assert.Same(section, definition.Section);
            Assert.Equal(confirmed, diameter.ConfirmedValue);
            Assert.Equal("120", diameter.PendingText);
            Assert.True(diameter.IsPending);
        }
        finally { release.TrySetResult(); }

        Assert.Null(await apply);
        Assert.False(definition.IsBusy);
        Assert.True(definition.ChangeFormCommand.CanExecute(null));
        Assert.Equal("120", diameter.PendingText);
        Assert.Equal(confirmed, diameter.ConfirmedValue);
        diameter.PendingText = "150";
        Assert.Equal("150", diameter.PendingText);
        Assert.True(diameter.Commit());
        Assert.NotEqual(confirmed, diameter.ConfirmedValue);
        Assert.False(diameter.IsPending);
        Assert.True(await definition.ConfirmAsync());
    }

    [Fact]
    public async Task AWorkspaceThatIsAlreadyBusyIsNotFrozenAndTheSettingsStayUnchanged()
    {
        var files = new Files(); var services = TestServices.Create(settingsStore: new(files, "settings"));
        var applying = CreateMain(files, services); var busy = CreateMain(files, services, new Dialogs());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeWrite = async (path, _, _) => { if (path != "settings") await release.Task; };
        var save = busy.SaveAsync(saveAs: true);
        Assert.True(busy.IsWorkspaceBusy);
        var settingsWrites = files.Writes.Count(path => path == "settings");

        Assert.Equal(Strings.ProjectBusy, await applying.ApplySettingsAsync(SettingsPatch.From(applying.Settings with { SignificantDigits = 5 })));
        Assert.Equal(UserSettings.Default.SignificantDigits, applying.Settings.SignificantDigits);
        Assert.Equal(settingsWrites, files.Writes.Count(path => path == "settings"));
        Assert.False(applying.IsWorkspaceBusy); Assert.True(applying.CanSaveProject);

        release.TrySetResult(); await save;
        Assert.Null(await applying.ApplySettingsAsync(SettingsPatch.From(applying.Settings with { SignificantDigits = 5 })));
        Assert.Equal(5, busy.Settings.SignificantDigits);
    }

    private static MainWindowViewModel CreateMain(Files? files = null, SharedServices? services = null, IProjectDialogs? dialogs = null)
    {
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, services: services ?? TestServices.Create(settingsStore: files is null ? null : new(files, "settings"))); main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var document = new EditorDocument(M(1), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(SupportId, M(.1), SupportType.Fixed, "A")],
            [new EditorPointForce(LoadId, M(.3), Force.FromNewtons(-100), "F1"),
             new EditorPointMoment(Guid.Parse("11111111-1111-1111-1111-111111111111"), M(.4), Moment.FromNewtonMeters(50), "M1")],
            distributedLoads: [new(Guid.Parse("22222222-2222-2222-2222-222222222222"), M(.2), M(.8), ForcePerLength.FromNewtonsPerMeter(-500), "q1")]);
        main.Editor!.Session.Commit(new(document, new()));
        return main;
    }

    private static void Edit(EditorViewModel editor, string target)
    {
        switch (target)
        {
            case "support": Assert.True(editor.EditSupport(SupportId)); editor.SupportDraft!.PositionText = "100"; break;
            case "force": Assert.True(editor.EditLoad(LoadId)); editor.LoadDraft!.ValueText = "-100"; break;
            case "moment": Assert.True(editor.EditLoad(editor.Document.Loads[1].Id)); editor.LoadDraft!.ValueText = "50"; break;
            case "distributed": Assert.True(editor.EditDistributedLoad(editor.Document.DistributedLoads[0].Id)); break;
            case "length": editor.DimensionLength.Begin(); editor.DimensionLength.Text = "1000"; break;
        }
    }

    [Theory]
    [InlineData("support")]
    [InlineData("force")]
    [InlineData("moment")]
    [InlineData("distributed")]
    [InlineData("length")]
    public async Task AffectedProfileChangesAreBlockedWithoutSavingOrDiscardingEitherTransaction(string target)
    {
        var files = new Files(); var main = CreateMain(files); var editor = main.Editor!;
        Edit(editor, target);
        var draft = (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft;
        var originalText = editor.DimensionLength.Text;
        var revision = main.Session!.CurrentRevision;
        var writes = files.Writes.ToArray();
        main.SetSettingsDialogOpen(true);
        editor.DimensionLength.LoseFocus();
        var dialog = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        dialog.Select(UnitProfileKind.UnitedStates);
        dialog.SelectedUiLanguage = UiLanguagePreference.English;
        dialog.SelectedRegionalFormat = RegionalFormatPreference.GermanGermany;
        Assert.False(await dialog.ApplyAsync());
        Assert.True(dialog.IsUnitedStates); Assert.True(dialog.CanApply); Assert.True(dialog.HasError);
        Assert.Equal(writes, files.Writes.ToArray());
        Assert.Same(revision, main.Session.CurrentRevision);
        Assert.Same(UnitProfile.Default, main.ResultPresentation.Profile);
        Assert.Same(draft, (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft);
        Assert.Equal(originalText, editor.DimensionLength.Text);
        Assert.False(main.SetResultPresentation(UnitProfile.StructuralEngineering, 5));
        editor.CancelEditorInteraction();
        Assert.True(await dialog.ApplyAsync());
        Assert.False(dialog.CanApply); Assert.True(main.Settings.ContentEquals(await new LocalSettingsStore(files, "settings").LoadAsync()));
        main.SetSettingsDialogOpen(false);
    }

    [Theory]
    [InlineData("support", QuantityKind.TransverseForce)]
    [InlineData("force", QuantityKind.Moment)]
    [InlineData("moment", QuantityKind.TransverseForce)]
    [InlineData("distributed", QuantityKind.TransverseForce)]
    [InlineData("length", QuantityKind.Moment)]
    public void ModeAndUnrelatedUnitChangesLeaveTheInputBuffersUntouched(string target, QuantityKind unrelated)
    {
        var main = CreateMain(); var editor = main.Editor!; Edit(editor, target);
        var support = editor.SupportDraft; var point = editor.LoadDraft; var distributed = editor.DistributedLoadDraft;
        var reference = editor.Presentation.Result;
        Assert.True(main.SetResultPresentation(UnitProfile.Default, 5));
        var unit = unrelated == QuantityKind.Moment ? UnitCatalog.KilonewtonMeter : UnitCatalog.Kilonewton;
        Assert.True(main.SetResultPresentation(UnitProfile.Default.WithUnit(unrelated, unit), 3));
        Assert.Same(support, editor.SupportDraft); Assert.Same(point, editor.LoadDraft); Assert.Same(distributed, editor.DistributedLoadDraft);
        Assert.Same(reference, editor.Presentation.Result);
        if (point is not null) Assert.Equal(target == "force" ? "N" : "N·m", point.Unit);
        if (support is not null) Assert.Equal("100", support.PositionText);
        if (target == "length") { Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("1000", editor.DimensionLength.Text); }
    }

    [Theory]
    [InlineData("support")]
    [InlineData("force")]
    [InlineData("moment")]
    [InlineData("distributed")]
    public void DraggingBlocksChangedInputUnitsAndUpdatesBuffersInTheOriginalUnits(string target)
    {
        var main = CreateMain(); var editor = main.Editor!;
        Assert.True(main.SetResultPresentation(UnitProfile.UnitedStates, 3));
        Edit(editor, target);
        Guid id = target switch { "support" => SupportId, "force" => LoadId, "moment" => editor.Document.Loads[1].Id, _ => editor.Document.DistributedLoads[0].Id };
        Assert.True(target switch
        {
            "support" => editor.BeginSupportDrag(id), "distributed" => editor.BeginDistributedLoadDrag(id, DistributedLoadEndpoint.Start),
            _ => editor.BeginLoadDrag(id)
        });
        Assert.False(main.SetResultPresentation(UnitProfile.Default, 3));
        var exact = M(.027387593197926163);
        if (target == "support")
        {
            editor.UpdateSupportDrag(exact); Assert.True(editor.EndSupportDrag());
            Assert.Equal(InputQuantityFormatter.FormatExact(exact.Meters, UnitCatalog.Foot), editor.SupportDraft!.PositionText);
            Assert.True(editor.ConfirmSupport()); Assert.Equal(exact, editor.Document.Supports[0].Position);
        }
        else if (target == "distributed")
        {
            editor.UpdateDistributedLoadDrag(exact); Assert.True(editor.EndDistributedLoadDrag());
            Assert.Equal(InputQuantityFormatter.FormatExact(exact.Meters, UnitCatalog.Foot), editor.DistributedLoadDraft!.StartText);
            Assert.True(editor.ConfirmDistributedLoad()); Assert.Equal(exact, editor.Document.DistributedLoads[0].StartPosition);
        }
        else
        {
            editor.UpdateLoadDrag(exact); Assert.True(editor.EndLoadDrag());
            Assert.Equal(InputQuantityFormatter.FormatExact(exact.Meters, UnitCatalog.Foot), editor.LoadDraft!.PositionText);
            Assert.True(editor.ConfirmLoad()); Assert.Equal(exact, editor.Document.Loads.Single(l => l.Id == id).Position);
        }
        Assert.True(main.SetResultPresentation(UnitProfile.Default, 3));
    }
}

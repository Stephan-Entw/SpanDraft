using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopSettingsTests
{
    [Theory]
    [InlineData(UnitProfileKind.MechanicalEngineering)]
    [InlineData(UnitProfileKind.StructuralEngineering)]
    [InlineData(UnitProfileKind.UnitedStates)]
    public void StandardSelectionUsesTheExistingDefinitions(UnitProfileKind kind)
    {
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        model.Select(kind);
        Assert.Same(UserSettings.Standard(kind), model.Pending.Profile);
        Assert.False(model.HasCustomProfile);
        Assert.Equal(7, model.LeftUnits.Count);
        Assert.Equal(6, model.RightUnits.Count);
        foreach (var row in model.LeftUnits.Concat(model.RightUnits))
            Assert.All(row.Units, unit => Assert.Equal(UnitCatalog.DimensionOf(row.Quantity), unit.Dimension));
        Assert.False(model.RightUnits.Single(r => r.Quantity == QuantityKind.Rotation).IsEditable);
    }

    [Fact]
    public void IndividualEditsRecognizeStandardsAndKeepTheLastCustomCombination()
    {
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        model.ChangeUnit(QuantityKind.BeamLength, UnitCatalog.Meter);
        Assert.True(model.IsCustom); Assert.True(model.HasCustomProfile);
        var custom = model.Pending.CustomProfile;
        Assert.Same(UnitCatalog.Newton, model.Pending.Profile[QuantityKind.TransverseForce]);
        model.Select(UnitProfileKind.UnitedStates);
        Assert.Same(custom, model.Pending.CustomProfile);
        model.Select(UnitProfileKind.Custom);
        Assert.Same(custom, model.Pending.Profile);
        model.ChangeUnit(QuantityKind.BeamLength, UnitCatalog.Millimeter);
        Assert.True(model.IsMechanical);
        Assert.Same(custom, model.Pending.CustomProfile);
        model.Select(UnitProfileKind.Custom);
        Assert.Same(custom, model.Pending.Profile);
    }

    [Fact]
    public async Task ApplyEstablishesANewBaselineAndCancelDiscardsOnlyLaterChanges()
    {
        UserSettings? published = null;
        var model = new SettingsViewModel(UserSettings.Default, settings =>
        { published = settings; return Task.FromResult<string?>(null); });
        Assert.False(model.CanApply);
        model.IsDetailed = true;
        Assert.True(model.CanApply);
        Assert.True(await model.ApplyAsync());
        Assert.Equal(PresentationMode.Detailed, published!.Mode);
        Assert.False(model.CanApply);
        model.Select(UnitProfileKind.StructuralEngineering);
        model.DiscardPending();
        Assert.True(model.IsMechanical); Assert.True(model.IsDetailed);
        Assert.False(model.HasChanges);
        model.ChangeUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter);
        model.ChangeUnit(QuantityKind.Moment, UnitCatalog.NewtonMeter);
        // Creation of the remembered individual profile remains a real preference change.
        Assert.True(model.HasChanges);
    }

    [Fact]
    public async Task SavingLocksFurtherEditsAndPreservesPendingStateOnFailure()
    {
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new SettingsViewModel(UserSettings.Default, _ => gate.Task);
        model.Select(UnitProfileKind.UnitedStates);
        var pending = model.Pending;
        var saving = model.ApplyAsync();
        Assert.True(model.IsSaving); Assert.False(model.CanApply);
        model.Select(UnitProfileKind.MechanicalEngineering); model.DiscardPending();
        Assert.Same(pending, model.Pending);
        Assert.False(await model.ApplyAsync());
        gate.SetResult("Write failed");
        Assert.False(await saving);
        Assert.True(model.CanApply); Assert.True(model.HasError);
        Assert.Same(pending, model.Pending);
        model.DiscardPending();
        Assert.Same(UserSettings.Default, model.Pending);
    }

    [Theory]
    [InlineData(UnitProfileKind.MechanicalEngineering)]
    [InlineData(UnitProfileKind.StructuralEngineering)]
    [InlineData(UnitProfileKind.UnitedStates)]
    [InlineData(UnitProfileKind.Custom)]
    public async Task StoreRoundtripIncludesTheRememberedCustomProfile(UnitProfileKind selected)
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        Assert.Same(UserSettings.Default, await store.LoadAsync());
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch)
            .WithUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter).Select(selected) with { Mode = PresentationMode.Detailed };
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();
        Assert.True(settings.ContentEquals(loaded));
        Assert.True(UserSettings.SameUnits(settings.CustomProfile!, loaded.Select(UnitProfileKind.Custom).Profile));
        Assert.DoesNotContain("scale", Encoding.UTF8.GetString(files.Data["settings"]), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("version")]
    [InlineData("unit")]
    [InlineData("dimension")]
    [InlineData("missingQuantity")]
    [InlineData("missingCustom")]
    [InlineData("profile")]
    [InlineData("lastProfile")]
    [InlineData("mode")]
    public async Task InvalidSettingsFallBackAsAWhole(string corruption)
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch);
        var json = JsonNode.Parse(SettingsCodec.Serialize(settings))!;
        switch (corruption)
        {
            case "version": json["version"] = 99; break;
            case "unit": json["customUnits"]!["BeamLength"] = "unknown"; break;
            case "dimension": json["customUnits"]!["BeamLength"] = "N"; break;
            case "missingQuantity": json["customUnits"]!.AsObject().Remove("BeamLength"); break;
            case "missingCustom": json["customUnits"] = null; break;
            case "profile": json["selectedProfile"] = "Unknown"; break;
            case "lastProfile": json["lastStandardProfile"] = "Custom"; break;
            case "mode": json["presentationMode"] = "Unknown"; break;
        }
        files.Data["settings"] = Encoding.UTF8.GetBytes(corruption switch
        { "broken" => "{", "null" => "null", "{}" => "{}", _ => json.ToJsonString() });
        Assert.Same(UserSettings.Default, await store.LoadAsync());
    }

    [Fact]
    public async Task StartupLoadsPreferencesBeforeTheFirstProjectAnalysis()
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var expected = UserSettings.Default.Select(UnitProfileKind.UnitedStates) with { Mode = PresentationMode.Detailed };
        await store.SaveAsync(expected);
        var main = new MainWindowViewModel(files: files, settingsStore: store);
        await main.InitializeSettingsAsync();
        Assert.True(expected.ContentEquals(main.Settings));
        main.Setup.ApplyCommand.Execute(null);
        Assert.True(UserSettings.SameUnits(expected.Profile, main.Editor!.ResultPresentation.Profile));
        Assert.Equal(PresentationMode.Detailed, main.Editor.ResultPresentation.Mode);
    }

    [Fact]
    public async Task FailedPublicationKeepsActivePreferencesAndThePreviousSettingsFile()
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        await store.SaveAsync(UserSettings.Default);
        var bytes = files.Data["settings"].ToArray();
        files.FailWritePath = "settings";
        var main = new MainWindowViewModel(files: files, settingsStore: store);
        var dialog = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        dialog.Select(UnitProfileKind.UnitedStates);
        Assert.False(await dialog.ApplyAsync());
        Assert.Contains(Strings.SettingsSaveError, dialog.ErrorText!);
        Assert.Equal(bytes, files.Data["settings"]);
        Assert.Same(UnitProfile.Default, main.ResultPresentation.Profile);
        Assert.True(dialog.IsUnitedStates); Assert.True(dialog.CanApply);
    }

    [Fact]
    public async Task PreferencePublicationLeavesProjectHistoryAnalysisAndRecoveryUntouched()
    {
        var files = new Files(); var delay = new Delay();
        string recoveryPath = Path.Combine(Path.GetTempPath(), "phase3-recovery.json");
        var recovery = new ProjectRecovery(files, recoveryPath, delay.Wait, () => Now);
        var store = new LocalSettingsStore(files, "settings");
        int analyses = 0;
        var main = new MainWindowViewModel(b => { analyses++; return SpanDraft.Analysis.BeamAnalysis.Analyze(b); },
            files: files, recovery: recovery, settingsStore: store);
        main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!; var session = editor.Session;
        session.MarkSaved(session.CurrentRevision, Path.Combine(Path.GetTempPath(), "phase3.spandraft"));
        editor.SetAnnotationOffset(Guid.NewGuid(), new(2, 3));
        session.Commit(session.CurrentRevision.State with { Document = editor.Document with { Length = M(1.2) } });
        delay.ReleaseAll(); await recovery.DrainAsync();
        var revision = session.CurrentRevision; var saved = session.SavedRevisionId;
        var undo = session.UndoHistory.ToArray(); var redo = session.RedoHistory.ToArray();
        var bytes = ProjectFileCodec.Serialize(revision.State); var recoveryBytes = files.Data[recoveryPath].ToArray();
        var result = editor.Presentation.Result; int count = analyses, changes = 0;
        session.Changed += () => changes++;
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Foot) with { Mode = PresentationMode.Detailed };
        Assert.Null(await main.ApplySettingsAsync(settings));
        Assert.Same(revision, session.CurrentRevision); Assert.Equal(saved, session.SavedRevisionId);
        Assert.Equal(undo, session.UndoHistory); Assert.Equal(redo, session.RedoHistory);
        Assert.True(session.IsDirty); Assert.Equal(bytes, ProjectFileCodec.Serialize(session.CurrentRevision.State));
        Assert.Equal(recoveryBytes, files.Data[recoveryPath]); Assert.Equal(count, analyses); Assert.Equal(0, changes);
        Assert.Same(result, editor.Presentation.Result);
        Assert.True(editor.Undo()); Assert.True(editor.Redo());
        Assert.True(UserSettings.SameUnits(settings.Profile, editor.ResultPresentation.Profile));
        delay.ReleaseAll(); await recovery.DrainAsync();
    }
}

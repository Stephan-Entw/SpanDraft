using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopResultPresentationTests
{
    private static ProjectState LoadedState()
    {
        var state = ProjectTestSupport.State();
        // The persistence fixture deliberately has an epsilon-sized final span.
        return state with { Document = state.Document with { Length = M(1) } };
    }
    internal static UnitProfile Mixed => UnitProfile.UnitedStates
        .WithUnit(QuantityKind.AxialForce, UnitCatalog.Newton)
        .WithUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter)
        .WithUnit(QuantityKind.TransverseDisplacement, UnitCatalog.Meter);

    public static IEnumerable<object[]> Cases()
    {
        foreach (string culture in new[] { "de-DE", "en-US" })
        foreach (PresentationMode mode in Enum.GetValues<PresentationMode>())
        foreach (int profile in Enumerable.Range(0, 4))
            yield return [culture, mode, profile];
    }

    internal static UnitProfile Profile(int index) => index switch
    {
        0 => UnitProfile.Default, 1 => UnitProfile.StructuralEngineering,
        2 => UnitProfile.UnitedStates, _ => Mixed
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ResultsReactionsAndSectionPropertiesUseThePhaseOnePipeline(string culture, PresentationMode mode, int index)
    {
        using var cultures = new ResultCultureScope(culture, culture == "de-DE" ? "en-US" : "de-DE");
        var profile = Profile(index);
        var state = LoadedState();
        var options = new ResultPresentationOptions(profile, mode);
        var editor = EditorViewModel.ForSession(ProjectSession.Create(state), () => { }, resultPresentation: options);
        var presentation = editor.Presentation;
        var result = presentation.Result!;
        string Format(double value, QuantityKind kind) => QuantityFormatter.Format(value, kind, profile, mode, presentation.References);
        string Number(double value, QuantityKind kind) => QuantityFormatter.FormatNumber(value, kind, profile, mode, presentation.References);
        Assert.Equal(Format(result.Engineering.TransverseDisplacementMagnitude.Meters, QuantityKind.TransverseDisplacement), presentation.Displacement);
        Assert.Equal(Format(result.Engineering.BendingMomentMagnitude.NewtonMeters, QuantityKind.Moment), presentation.Moment);
        Assert.Equal(Format(result.Engineering.MaximumBendingStress.Pascals, QuantityKind.Stress), presentation.Stress);
        Assert.Equal(Format(result.Engineering.SafetyFactor, QuantityKind.SafetyFactor), presentation.SafetyFactor);
        foreach (var support in editor.Document.Supports)
        {
            var node = result.Solution.Nodes.Single(n => n.Position == support.Position);
            var row = editor.Overview.Reactions.Single(r => r.Id == support.Id);
            Assert.Equal(node.ReactionX is { } x ? Number(x.Newtons, QuantityKind.AxialForce) : "", row.Rx);
            Assert.Equal(node.ReactionY is { } y ? Number(y.Newtons, QuantityKind.TransverseForce) : "", row.Ry);
            Assert.Equal(node.ReactionMoment is { } m ? Number(m.NewtonMeters, QuantityKind.Moment) : "", row.Moment);
        }
        Assert.Equal($"Rx [{profile[QuantityKind.AxialForce].Symbol}]", editor.Overview.ReactionXHeader);
        Assert.Equal($"Ry [{profile[QuantityKind.TransverseForce].Symbol}]", editor.Overview.ReactionYHeader);
        Assert.Equal($"M [{profile[QuantityKind.Moment].Symbol}]", editor.Overview.ReactionMomentHeader);
        var setup = new ProjectSetupViewModel(ProjectSetupMode.Edit, state.Document.Section, state.Document.BendingAxis, state.Document.Material,
            _ => { }, () => { }, options);
        Assert.Equal(Format(setup.SelectedSection.Area.SquareMeters, QuantityKind.Area), setup.Area);
        Assert.Equal(Format(setup.SelectedSection.GetAxis(setup.BendingAxis).SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea), setup.Inertia);
        Assert.Equal(Format(setup.SelectedSection.GetAxis(setup.BendingAxis).PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus), setup.Modulus);
        Assert.Equal(UiNumbers.Indicator(state.Document.Material.YoungsModulus.Pascals / 1e9) + " GPa", setup.YoungsModulus);
        Assert.Equal(UiNumbers.Indicator(state.Document.Material.YieldStrength.Megapascals) + " MPa", setup.YieldStrength);
    }

    [Fact]
    public async Task SwitchingRetainsAnalysisReferencesHistorySavepointRecoveryAndOpenDrafts()
    {
        var app = new App();
        var editor = app.Main.Editor!;
        var session = app.Main.Session!;
        session.MarkSaved(session.CurrentRevision, Path.Combine(Path.GetTempPath(), "presentation.spandraft"));
        Assert.True(session.Commit(LoadedState()));
        var state = session.CurrentRevision.State;
        Assert.True(session.Commit(state with { Document = state.Document with { Length = M(1.2) } }));
        Assert.True(session.Undo());
        app.Delay.ReleaseAll();
        await app.Recovery.DrainAsync();
        Assert.True(editor.EditLoad(LoadId));
        var draft = editor.LoadDraft!;
        draft.NameText = "Uncommitted name";
        draft.PositionText = "bad position";
        draft.ValueText = "-";
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = "unfinished";
        var presentation = editor.Presentation;
        Assert.NotNull(presentation.Result);
        var references = presentation.References;
        var revision = session.CurrentRevision;
        var document = editor.Document;
        var undo = session.UndoHistory.ToArray();
        var redo = session.RedoHistory.ToArray();
        var savepoint = session.SavedRevisionId;
        var path = session.FilePath;
        var dirty = session.IsDirty;
        var bytes = ProjectFileCodec.Serialize(revision.State);
        var writes = app.Files.Writes.ToArray();
        var recovery = app.Files.Data[App.Slot].ToArray();
        int waiters = app.Delay.Waiters.Count, analyses = app.Analyses, changed = 0, applied = 0;
        var inputRows = editor.Overview.Loads.ToArray();
        var notifications = new List<string?>();
        session.Changed += () => changed++;
        session.StateApplied += (_, _, _) => applied++;
        editor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        foreach (var profile in new[] { UnitProfile.UnitedStates, Mixed, UnitProfile.Default })
        foreach (var mode in Enum.GetValues<PresentationMode>())
        {
            Assert.Equal(UserSettings.SameUnits(profile, UnitProfile.Default), app.Main.SetResultPresentation(profile, mode));
            Assert.True(app.Main.SetResultPresentation(UnitProfile.Default, mode));
            Assert.Same(presentation.Result, editor.Presentation.Result);
            Assert.Same(references, editor.Presentation.References);
            Assert.Same(revision, session.CurrentRevision);
            Assert.Same(document, editor.Document);
            Assert.Equal(undo, session.UndoHistory);
            Assert.Equal(redo, session.RedoHistory);
            Assert.Equal(savepoint, session.SavedRevisionId);
            Assert.Equal(path, session.FilePath);
            Assert.Equal(dirty, session.IsDirty);
            Assert.False(session.IsBusy);
            Assert.Equal(bytes, ProjectFileCodec.Serialize(session.CurrentRevision.State));
            Assert.Same(draft, editor.LoadDraft);
            Assert.Equal("Uncommitted name", draft.NameText);
            Assert.Equal("bad position", draft.PositionText);
            Assert.Equal("-", draft.ValueText);
            Assert.Equal("unfinished", editor.DimensionLength.Text);
            Assert.Equal(inputRows, editor.Overview.Loads);
            Assert.Equal(writes, app.Files.Writes);
            Assert.Equal(recovery, app.Files.Data[App.Slot]);
            Assert.Equal(waiters, app.Delay.Waiters.Count);
            Assert.Equal(analyses, app.Analyses);
        }
        Assert.Equal(0, changed);
        Assert.Equal(0, applied);
        Assert.All(notifications, name => Assert.Contains(name, new[] { "ResultPresentation", "Presentation", "Overview", "CoordinateText" }));
        var before = editor.Presentation;
        int count = notifications.Count;
        app.Main.SetResultPresentation(UnitProfile.Default, PresentationMode.Detailed);
        Assert.Same(before, editor.Presentation);
        Assert.Equal(count, notifications.Count);
        Assert.True(session.Commit(state with { Document = document with { Length = M(1.3) } }));
        Assert.Equal(analyses + 1, app.Analyses);
        Assert.NotSame(before.Result, editor.Presentation.Result);
        Assert.NotSame(references, editor.Presentation.References);
        Assert.Equal(PresentationMode.Detailed, editor.ResultPresentation.Mode);
        app.Delay.ReleaseAll();
        await app.Recovery.DrainAsync();
    }

    [Fact]
    public async Task OptionsSurviveCreationNavigationOpenNewAndRecovery()
    {
        var app = new App(create: false);
        app.Main.SetResultPresentation(Mixed, PresentationMode.Detailed);
        var section = app.Main.Setup.SelectedSection;
        var material = app.Main.Setup.SelectedMaterial;
        Assert.Same(section, app.Main.Setup.SelectedSection);
        Assert.Same(material, app.Main.Setup.SelectedMaterial);
        app.Main.Setup.ApplyCommand.Execute(null);
        Assert.Equal(app.Main.ResultPresentation, app.Main.Editor!.ResultPresentation);
        app.Dialogs.Leave = LeaveDecision.Discard;
        app.Dialogs.OpenPath = Path.Combine(Path.GetTempPath(), "presentation-open.spandraft");
        app.Files.Data[app.Dialogs.OpenPath] = ProjectFileCodec.Serialize(LoadedState());
        Assert.True(await app.Main.OpenAsync());
        Assert.Equal(app.Main.ResultPresentation, app.Main.Editor!.ResultPresentation);
        app.Main.EditProject();
        var setup = app.Main.Setup;
        section = setup.SelectedSection; material = setup.SelectedMaterial;
        var materialText = (setup.YoungsModulus, setup.YieldStrength);
        var notifications = new List<string?>();
        setup.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        int analyses = app.Analyses;
        app.Main.SetResultPresentation(UnitProfile.UnitedStates, PresentationMode.Standard);
        Assert.Equal(new[] { "ResultPresentation", "SectionName", "SelectedAxisChoice", "BendingAxis", "Area", "Inertia", "Modulus",
            "InertiaLabel", "ModulusLabel", "IsAsymmetric", "PositiveModulus", "NegativeModulus" }, notifications);
        Assert.Same(section, setup.SelectedSection);
        Assert.Same(material, setup.SelectedMaterial);
        Assert.Equal(materialText, (setup.YoungsModulus, setup.YieldStrength));
        setup.CancelCommand.Execute(null);
        Assert.Equal(analyses, app.Analyses);
        Assert.True(await app.Main.NewAsync());
        Assert.Equal(app.Main.ResultPresentation, app.Main.Setup.ResultPresentation);
        app.Main.Setup.ApplyCommand.Execute(null);
        Assert.Equal(app.Main.ResultPresentation, app.Main.Editor!.ResultPresentation);
        var restored = new App(create: false);
        restored.Main.SetResultPresentation(Mixed, PresentationMode.Detailed);
        restored.Files.Data[App.Slot] = ProjectRecovery.Encode(new(LoadedState(), app.Dialogs.OpenPath, Now));
        Assert.True(await restored.Main.InitializeRecoveryAsync());
        Assert.Equal(restored.Main.ResultPresentation, restored.Main.Editor!.ResultPresentation);
        Assert.NotNull(restored.Main.Editor.Presentation.Result);
        app.Delay.ReleaseAll(); restored.Delay.ReleaseAll();
        await app.Recovery.DrainAsync(); await restored.Recovery.DrainAsync();
    }

    [Fact]
    public void ZeroApproximateZeroFreeReactionsAndInfinityRemainDistinct()
    {
        var document = new EditorDocument(M(1), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(1), SupportType.Roller, "B")]);
        foreach (var profile in new[] { UnitProfile.Default, UnitProfile.StructuralEngineering, UnitProfile.UnitedStates, Mixed })
        foreach (var mode in Enum.GetValues<PresentationMode>())
        {
            var options = new ResultPresentationOptions(profile, mode);
            var editor = new EditorViewModel(document, () => { }, resultPresentation: options);
            Assert.Equal("∞", editor.Presentation.SafetyFactor);
            Assert.Equal("0 " + profile[QuantityKind.TransverseDisplacement].Symbol, editor.Presentation.Displacement);
            Assert.Equal("0", editor.Overview.Reactions[0].Rx);
            Assert.Equal("", editor.Overview.Reactions[1].Rx);
            Assert.All(editor.Overview.Reactions, r => { Assert.Equal("0", r.Ry); Assert.Empty(r.Moment); });
            var tiny = document.WithLoads([new EditorPointForce(Guid.NewGuid(), M(.5), Force.FromNewtons(-1e-8), "F1")]);
            var loaded = new EditorViewModel(tiny, () => { }, resultPresentation: options);
            if (mode == PresentationMode.Standard) Assert.StartsWith("≈ 0 ", loaded.Presentation.Displacement);
            else Assert.DoesNotContain("≈", loaded.Presentation.Displacement);
        }
    }

    [Fact]
    public void FailureAfterSuccessCannotRecoverStaleValuesThroughAChangeOfPresentation()
    {
        var session = ProjectSession.Create(LoadedState());
        var editor = EditorViewModel.ForSession(session, () => { });
        Assert.NotNull(editor.Presentation.Result);
        Assert.True(session.Commit(session.CurrentRevision.State with { Document = editor.Document.WithSupports([]) }));
        var failed = editor.Presentation;
        var updated = failed.WithPresentation(new(Mixed, PresentationMode.Detailed));
        Assert.Null(updated.Result);
        Assert.Null(updated.References);
        Assert.Empty(updated.Displacement); Assert.Empty(updated.Moment);
        Assert.Empty(updated.Stress); Assert.Empty(updated.SafetyFactor);
        Assert.Empty(editor.Overview.Reactions);
    }

    [Fact]
    public void InvalidOptionsAreRejectedAndIdenticalDefaultIsANoOp()
    {
        var app = new App(create: false);
        var options = app.Main.ResultPresentation;
        int changes = 0;
        app.Main.PropertyChanged += (_, _) => changes++;
        app.Main.SetResultPresentation(UnitProfile.Default, PresentationMode.Standard);
        Assert.Same(options, app.Main.ResultPresentation); Assert.Equal(0, changes);
        Assert.Throws<ArgumentNullException>(() => app.Main.SetResultPresentation(null!, PresentationMode.Standard));
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Main.SetResultPresentation(UnitProfile.Default, (PresentationMode)42));
        Assert.Same(options, app.Main.ResultPresentation);
    }

    [Fact]
    public void SwitchingBeforeFirstReferenceAccessRetainsTheLazyCache()
    {
        var outcome = BeamAnalysis.Analyze(LoadedState().Document.ToBeamModel());
        var original = AnalysisPresentationState.FromOutcome(outcome);
        var switched = original.WithPresentation(new(Mixed, PresentationMode.Detailed));
        Assert.Same(outcome.Result, switched.Result);
        Assert.Same(switched.References, original.References);
        var nextAnalysis = AnalysisPresentationState.FromOutcome(BeamAnalysis.Analyze(LoadedState().Document.ToBeamModel()));
        Assert.NotSame(switched.References, nextAnalysis.References);
    }

    [Fact]
    public void NumberOnlyFormattingUsesTheSamePrecisionAndExplicitCultureOverride()
    {
        using var cultures = new ResultCultureScope("de-DE", "en-US");
        var references = new ModelReferenceValues(1, 1234.5678, 1234.5678, 235e6);
        foreach (var mode in Enum.GetValues<PresentationMode>())
        foreach (var profile in new[] { UnitProfile.Default, UnitProfile.StructuralEngineering, UnitProfile.UnitedStates, Mixed })
        foreach (var kind in new[] { QuantityKind.AxialForce, QuantityKind.TransverseForce, QuantityKind.Moment })
        foreach (double value in new[] { 0, -0.0, 1e-12, -1234.5678 })
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            string number = QuantityFormatter.FormatNumber(value, kind, profile, mode, references, culture);
            Assert.Equal(number + " " + profile[kind].Symbol, QuantityFormatter.Format(value, kind, profile, mode, references, culture));
            Assert.DoesNotContain(",", number);
        }
        Assert.Equal("∞", QuantityFormatter.FormatNumber(double.PositiveInfinity, QuantityKind.SafetyFactor));
        Assert.Equal("2,5", UiNumbers.AxisTick(1, 2.5, 0, CultureInfo.CurrentCulture));
        Assert.Equal("2.5", UiNumbers.AxisTick(1, 2.5, 0));
    }
}

internal sealed class ResultCultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    public ResultCultureScope(string culture, string uiCulture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
    }
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}

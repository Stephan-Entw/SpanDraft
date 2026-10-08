using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopSettingsInteractionTests
{
    private static MainWindowViewModel CreateMain(Files? files = null)
    {
        var main = new MainWindowViewModel(files: files, settingsStore: files is null ? null : new(files, "settings"));
        main.Setup.ApplyCommand.Execute(null);
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
        main.SetSettingsDialogOpen(true);
        editor.DimensionLength.LoseFocus();
        var dialog = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        dialog.Select(UnitProfileKind.UnitedStates);
        Assert.False(await dialog.ApplyAsync());
        Assert.True(dialog.IsUnitedStates); Assert.True(dialog.CanApply); Assert.True(dialog.HasError);
        Assert.Empty(files.Writes);
        Assert.Same(revision, main.Session.CurrentRevision);
        Assert.Same(UnitProfile.Default, main.ResultPresentation.Profile);
        Assert.Same(draft, (object?)editor.SupportDraft ?? (object?)editor.LoadDraft ?? editor.DistributedLoadDraft);
        Assert.Equal(originalText, editor.DimensionLength.Text);
        Assert.False(main.SetResultPresentation(UnitProfile.StructuralEngineering, PresentationMode.Detailed));
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
        Assert.True(main.SetResultPresentation(UnitProfile.Default, PresentationMode.Detailed));
        var unit = unrelated == QuantityKind.Moment ? UnitCatalog.KilonewtonMeter : UnitCatalog.Kilonewton;
        Assert.True(main.SetResultPresentation(UnitProfile.Default.WithUnit(unrelated, unit), PresentationMode.Standard));
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
        Assert.True(main.SetResultPresentation(UnitProfile.UnitedStates, PresentationMode.Standard));
        Edit(editor, target);
        Guid id = target switch { "support" => SupportId, "force" => LoadId, "moment" => editor.Document.Loads[1].Id, _ => editor.Document.DistributedLoads[0].Id };
        Assert.True(target switch
        {
            "support" => editor.BeginSupportDrag(id), "distributed" => editor.BeginDistributedLoadDrag(id, DistributedLoadEndpoint.Start),
            _ => editor.BeginLoadDrag(id)
        });
        Assert.False(main.SetResultPresentation(UnitProfile.Default, PresentationMode.Standard));
        var exact = M(.027387593197926163);
        if (target == "support")
        {
            editor.UpdateSupportDrag(exact); Assert.True(editor.EndSupportDrag());
            Assert.Equal(InputQuantityFormatter.Format(exact.Meters, UnitCatalog.Foot), editor.SupportDraft!.PositionText);
            Assert.True(editor.ConfirmSupport()); Assert.Equal(exact, editor.Document.Supports[0].Position);
        }
        else if (target == "distributed")
        {
            editor.UpdateDistributedLoadDrag(exact); Assert.True(editor.EndDistributedLoadDrag());
            Assert.Equal(InputQuantityFormatter.Format(exact.Meters, UnitCatalog.Foot), editor.DistributedLoadDraft!.StartText);
            Assert.True(editor.ConfirmDistributedLoad()); Assert.Equal(exact, editor.Document.DistributedLoads[0].StartPosition);
        }
        else
        {
            editor.UpdateLoadDrag(exact); Assert.True(editor.EndLoadDrag());
            Assert.Equal(InputQuantityFormatter.Format(exact.Meters, UnitCatalog.Foot), editor.LoadDraft!.PositionText);
            Assert.True(editor.ConfirmLoad()); Assert.Equal(exact, editor.Document.Loads.Single(l => l.Id == id).Position);
        }
        Assert.True(main.SetResultPresentation(UnitProfile.Default, PresentationMode.Standard));
    }
}

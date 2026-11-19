using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class SectionDefinitionTransitionTests : SectionDefinitionPrototypeTestSupport
{
    [Fact]
    public void LiveDraftUpdatesGeometryPropertiesAndAxesWithoutChangingConfirmedState()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Fill(vm, false); Commit(vm, "h", "120");
        var section = vm.Section; var input = Input(vm, "b");
        input.PendingText = "150";
        Assert.Same(section, vm.Section); Assert.Equal(.12, input.ConfirmedValue); Assert.True(input.IsPending);
        Assert.Equal(.15, vm.Preview!.Width); Assert.Equal(.12, vm.Preview.Height);
        Assert.False(vm.Preview.Dimensions["b"].IsConfirmed); Assert.True(vm.Preview.Dimensions["h"].IsConfirmed);
        Assert.True(vm.Preview.Dimensions["b"].IsPending);
        var drawing = SectionPreviewDrawingLayout.Create(vm.Preview, new(520, 440), UnitCatalog.Millimeter, text => new(text.PlainText.Length * 7, 16));
        Assert.Contains("150", drawing.Dimensions.Single(d => d.Key == "b").Text);
        Assert.True(vm.RequiresAxisSelection); Assert.False(vm.AxesEquivalent); Assert.Equal(2, vm.AxisValues.Count);
        Assert.All(vm.AxisValues, block => { Assert.True(block.IsSelectable); Assert.False(block.IsSelected); });
        Assert.Contains("150", vm.HeaderTitle); Assert.False(input.HasError); Assert.False(vm.CanConfirm);
        Assert.Equal(SectionDisplay.Property(false, .15 * .12, QuantityKind.Area, vm.Presentation), vm.Area);
        input.PendingText = "120";
        Assert.True(vm.AxesEquivalent); Assert.Single(vm.AxisValues); Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal(.12, vm.Preview!.Width); Assert.False(vm.Preview.Dimensions["b"].IsPending);
        Assert.DoesNotContain("150", vm.HeaderTitle); Assert.Equal(.12, input.ConfirmedValue);
    }

    [Fact]
    public void PendingValueInvalidatesOnlyDisplayedSelectionAndSameAxisCanBeSelectedAgain()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
        var input = Input(vm, "h"); var axis = vm.SelectedAxis;
        input.PendingText = "240"; Assert.Null(vm.SelectedAxis); Assert.All(vm.AxisValues, b => Assert.False(b.IsSelected));
        input.PendingText = "200"; Assert.Equal(axis, vm.SelectedAxis);
        input.PendingText = "240"; vm.SelectedAxis = axis;
        Assert.False(input.IsPending); Assert.Equal(.24, input.ConfirmedValue); Assert.Equal(axis, vm.SelectedAxis);
        Assert.True(vm.CanConfirm);
        Assert.True(vm.Undo()); Assert.Null(vm.SelectedAxis); Assert.Equal(.24, Input(vm, "h").ConfirmedValue);
        Assert.True(vm.Undo()); Assert.Equal(axis, vm.SelectedAxis); Assert.Equal(.2, Input(vm, "h").ConfirmedValue);
        Assert.True(vm.Redo()); Assert.Null(vm.SelectedAxis); Assert.True(vm.Redo()); Assert.Equal(axis, vm.SelectedAxis);
    }

    [Theory]
    [InlineData(SectionEditorType.Rectangle, "b", "-", "150")]
    [InlineData(SectionEditorType.CircularHollow, "t", "40", "6")]
    public void InvalidIntermediateDraftKeepsMarkedPreviewAndDisablesStaleAxisValues(
        SectionEditorType type, string key, string invalid, string valid)
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm);
        var input = Input(vm, key); input.PendingText = valid;
        var preview = vm.Preview; var blocks = vm.AxisValues.ToArray(); var section = vm.Section;
        input.PendingText = invalid;
        Assert.Same(preview, vm.Preview); Assert.Same(section, vm.Section); Assert.True(vm.PreviewIsStale);
        Assert.Equal("—", vm.Area); Assert.Equal(blocks, vm.AxisValues);
        Assert.All(vm.AxisValues, block => { Assert.Equal("—", block.Inertia); Assert.False(block.IsSelectable); Assert.False(block.IsSelected); });
        Assert.False(vm.RequiresAxisSelection); Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(input.HasError);
        vm.SelectedAxis = SectionAxisDesignation.Z; Assert.True(input.IsPending);
        input.PendingText = valid;
        Assert.False(vm.PreviewIsStale); Assert.NotEqual("—", vm.Area); Assert.Same(section, vm.Section);
    }

    [Fact]
    public void DraftEvaluationFollowsFormChangesPresentationAndRepeatedCommits()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
        Input(vm, "b").PendingText = "150";
        vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        Assert.False(vm.RequiresAxisSelection); Assert.Single(vm.AxisValues); Assert.Equal(.08, vm.Preview!.Width);
        vm.SelectForm(SectionEditorType.Rectangle);
        Assert.True(Input(vm, "b").IsPending); Assert.Equal(.15, vm.Preview!.Width); Assert.True(vm.RequiresAxisSelection);
        vm.ApplyResultPresentation(new(significantDigits: 1));
        Assert.Equal(SectionDisplay.Property(false, .15 * .2, QuantityKind.Area, vm.Presentation), vm.Area);
        Assert.Contains("150", vm.DisplayName);
        vm.SelectedAxis = SectionAxisDesignation.Z;
        Assert.True(vm.CanConfirm); Assert.All(vm.Preview.Dimensions.Values, d => { Assert.True(d.IsConfirmed); Assert.False(d.IsPending); });
        Input(vm, "b").PendingText = "160"; Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm);
        vm.SelectedAxis = SectionAxisDesignation.Y;
        Assert.True(vm.CanConfirm); Assert.Equal(.16, Input(vm, "b").ConfirmedValue);
        vm.BeginNameEdit(); vm.PendingName = "Custom"; Assert.True(vm.CommitName());
        Input(vm, "b").PendingText = "170"; Assert.Equal("Custom", vm.HeaderTitle);
    }

    [Theory]
    [InlineData(SectionEditorType.Rectangle, "h", "120")]
    [InlineData(SectionEditorType.CircularHollow, "t", "6")]
    [InlineData(SectionEditorType.Manual, "WY", "150000")]
    public async Task ApplyAcceptsOneValidPendingValueWithoutRequiredAxis(SectionEditorType type, string key, string text)
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm, false);
        var input = Input(vm, key); input.PendingText = text;
        var section = vm.Section; var preview = vm.Preview; var name = vm.DisplayName;
        Assert.False(vm.CanConfirm); Assert.True(vm.CanSaveLibrary);
        Assert.True(vm.ConfirmCommand.CanExecute(null)); Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Same(section, vm.Section); Assert.Same(preview, vm.Preview); Assert.Equal(name, vm.DisplayName);
        Assert.True(input.IsPending);

        Assert.True(await vm.ConfirmAsync());

        Assert.False(input.IsPending); Assert.True(vm.CanConfirm); Assert.False(vm.RequiresAxisSelection);
        Assert.NotSame(section, vm.Section);
        if (type == SectionEditorType.Rectangle)
        {
            var rectangle = Assert.IsType<RectangleSectionGeometry>(vm.Section);
            Assert.Equal(.12, rectangle.Width.Meters); Assert.Equal(.12, rectangle.Height.Meters);
        }
    }

    [Fact]
    public async Task PendingAsymmetricSectionRequiresCommitAndExplicitAxisSelection()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Commit(vm, "b", "120"); var height = Input(vm, "h"); height.PendingText = "200";
        Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(await vm.ConfirmAsync());
        Assert.True(vm.SaveLibraryCommand.CanExecute(null));
        Assert.True(height.IsPending); Assert.Null(height.ConfirmedValue);
        Assert.True(height.Commit()); Assert.True(vm.RequiresAxisSelection);
        Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(await vm.ConfirmAsync());
        vm.SelectedAxis = SectionAxisDesignation.Z;
        Assert.True(vm.ConfirmCommand.CanExecute(null)); Assert.True(await vm.ConfirmAsync());
    }

    [Fact]
    public async Task PendingChangeFromSquareToRectangleDoesNotUseOldEquivalentAxesOrSelection()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Fill(vm, false); Commit(vm, "h", "120"); Assert.True(vm.AxesEquivalent);
        var square = vm.Section; var height = Input(vm, "h"); height.PendingText = "200";
        Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(await vm.ConfirmAsync());
        Assert.Same(square, vm.Section); Assert.True(height.IsPending); Assert.Equal(.12, height.ConfirmedValue);
        Assert.True(height.Commit()); vm.SelectedAxis = SectionAxisDesignation.Y;
        height.PendingText = "201";
        Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(await vm.ConfirmAsync());
        Assert.Null(vm.SelectedAxis); Assert.True(height.IsPending);
        height.PendingText = "200"; Assert.Equal(SectionAxisDesignation.Y, vm.SelectedAxis);
    }

    [Theory]
    [InlineData("invalid")] [InlineData("0")] [InlineData("-1")] [InlineData("40")]
    public async Task InvalidPendingValueCannotApplyOrChangeErrorState(string text)
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.CircularHollow); Fill(vm, false);
        var input = Input(vm, "t"); var section = vm.Section; var preview = vm.Preview;
        input.PendingText = text;
        Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(await vm.ConfirmAsync());
        Assert.False(vm.SaveLibraryCommand.CanExecute(null)); Assert.False(await vm.SaveLibraryAsync());
        Assert.Same(section, vm.Section); Assert.Same(preview, vm.Preview);
        Assert.Equal(.005, input.ConfirmedValue); Assert.True(input.IsPending);
        Assert.False(input.HasError); Assert.False(input.HasParseError);
    }

    [Theory]
    [InlineData("invalid")] [InlineData("40")]
    public async Task ValidPendingCorrectionLeavesExistingErrorUntouchedUntilApply(string invalid)
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.CircularHollow); Fill(vm, false);
        var input = Input(vm, "t"); input.PendingText = invalid; Assert.False(input.Commit());
        var error = input.Error; bool parseError = input.HasParseError; var confirmed = input.ConfirmedValue;
        input.PendingText = "6"; var preview = vm.Preview;
        Assert.True(vm.ConfirmCommand.CanExecute(null)); Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal(error, input.Error); Assert.Equal(parseError, input.HasParseError);
        Assert.Equal(confirmed, input.ConfirmedValue); Assert.Null(vm.Section); Assert.Same(preview, vm.Preview);
        Assert.True(input.IsPending); Assert.False(vm.CanConfirm);

        Assert.True(await vm.ConfirmAsync());

        Assert.False(input.HasError); Assert.False(input.HasParseError); Assert.False(input.IsPending);
        Assert.Equal(.006, Assert.IsType<CircularHollowSectionGeometry>(vm.Section).WallThickness.Meters);
    }

    [Fact]
    public async Task ApplyDoesNotBulkCommitMultiplePendingFields()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle);
        Input(vm, "b").PendingText = "120"; Input(vm, "h").PendingText = "120";
        Assert.False(vm.ConfirmCommand.CanExecute(null)); Assert.False(await vm.ConfirmAsync());
        Assert.All(vm.Parameters, input => { Assert.True(input.IsPending); Assert.Null(input.ConfirmedValue); Assert.False(input.HasError); });
    }

    [Fact]
    public async Task ApplyPreservesSelectedAxisWhenPendingTextHasSameSiValue()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Rectangle); Fill(vm);
        var axis = vm.SelectedAxis; var input = Input(vm, "h"); input.PendingText = " 200 ";
        Assert.False(vm.CanConfirm); Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.True(await vm.ConfirmAsync());
        Assert.Equal(axis, vm.SelectedAxis); Assert.True(vm.CanConfirm);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var type in Enum.GetValues<SectionEditorType>())
        foreach (var configuration in type == SectionEditorType.Manual
            ? Enum.GetValues<ManualAxisConfiguration>() : new[] { ManualAxisConfiguration.Y })
        foreach (string culture in new[] { "de-DE", "en-US" })
        foreach (bool us in new[] { false, true })
            yield return [type, configuration, culture, us];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryInputTransitionUpdatesDraftButPreservesConfirmedValuesAndRefreshesCommands(
        SectionEditorType type, ManualAxisConfiguration configuration, string culture, bool us)
    {
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(new(us ? UnitProfile.UnitedStates : UnitProfile.Default));
        vm.SelectForm(type);
        if (type == SectionEditorType.Manual)
            vm.Configuration = vm.Configurations.Single(c => c.Value == configuration);
        Assert.True(vm.ShowLibraryAction);
        Assert.False(vm.CanSaveLibrary);
        Assert.False(vm.SaveLibraryCommand.CanExecute(null));
        var parameters = vm.Parameters;
        foreach (var input in parameters)
        {
            var section = vm.Section;
            var confirmed = input.ConfirmedValue;
            input.PendingText = input.Key switch
            {
                "b" => "120", "h" => "200", "d" or "D" => "80", "t" or "tw" => "5",
                "tf" => "12", "r" => "8", "A" => "7200",
                _ => input.Key.StartsWith('I') ? "8640000" : "144000"
            };
            Assert.Same(section, vm.Section); Assert.Equal(confirmed, input.ConfirmedValue); Assert.False(vm.CanConfirm);
            if (vm.Preview is { } draft)
            {
                Assert.True(draft.Dimensions[input.Key].IsPending);
                Assert.False(draft.Dimensions[input.Key].IsConfirmed);
            }
            bool completesSection = input == parameters[^1];
            Assert.Equal(completesSection, vm.CanSaveLibrary);
            Assert.Equal(completesSection, vm.SaveLibraryCommand.CanExecute(null));
            input.Commit();
            if (vm.Preview is { } committed) Assert.True(committed.Dimensions[input.Key].IsConfirmed);
        }
        if (vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[0].Value;
        Assert.True(vm.HasValidSection);
        Assert.True(vm.CanConfirm);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.True(vm.CanSaveLibrary);
        foreach (var input in vm.Parameters.ToArray())
        {
            string valid = input.PendingText;
            var preview = vm.Preview;
            var state = vm.LayoutState;
            input.PendingText = "pending";
            Assert.Same(preview, vm.Preview);
            Assert.Equal(state, vm.LayoutState);
            Assert.True(vm.ShowLibraryAction);
            Assert.False(vm.CanSaveLibrary);
            Assert.False(vm.ConfirmCommand.CanExecute(null));
            input.PendingText = "invalid";
            Assert.False(input.Commit());
            Assert.True(input.HasError);
            Assert.True(vm.ShowLibraryAction);
            Assert.False(vm.CanSaveLibrary);
            Assert.False(vm.SaveLibraryCommand.CanExecute(null));
            input.PendingText = valid;
            Assert.True(input.Commit());
            Assert.False(input.HasError);
            Assert.True(vm.HasValidSection);
            if (vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[0].Value;
            Assert.True(vm.CanConfirm);
            Assert.True(vm.CanSaveLibrary);
        }
        vm.EditNameCommand.Execute(null);
        vm.PendingName = "  " + new string('W', 80);
        Assert.True(vm.CommitName());
        vm.ResetNameCommand.Execute(null);
        Assert.True(vm.CanConfirm);
    }
}

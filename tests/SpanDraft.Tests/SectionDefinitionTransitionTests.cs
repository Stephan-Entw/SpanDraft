using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class SectionDefinitionTransitionTests : SectionDefinitionPrototypeTestSupport
{
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
    public void EveryInputTransitionPreservesCommittedPreviewAndRefreshesCommands(
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
        foreach (var input in vm.Parameters.ToArray())
        {
            var preview = vm.Preview;
            var state = vm.LayoutState;
            input.PendingText = input.Key switch
            {
                "b" => "120", "h" => "200", "d" or "D" => "80", "t" or "tw" => "5",
                "tf" => "12", "r" => "8", "A" => "7200",
                _ => input.Key.StartsWith('I') ? "8640000" : "144000"
            };
            Assert.Same(preview, vm.Preview);
            Assert.Equal(state, vm.LayoutState);
            Assert.False(vm.CanSaveLibrary);
            Assert.False(vm.SaveLibraryCommand.CanExecute(null));
            input.Commit();
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

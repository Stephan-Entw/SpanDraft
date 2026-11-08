using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class MaterialStrengthControlTests : MaterialStrengthTestSupport
{

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void EditorRequiresChoiceKeepsValueAndCategoryIndependentAndUpdatesAccessibleLabels(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel();
        main.NewCommand.Execute(null);
        var editor = new MaterialEditorViewModel(main.Setup, null, SetupEditorMode.ProjectDraft);
        editor.Name = "Custom"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "235";
        Assert.Null(editor.StrengthType); Assert.Null(editor.Draft); Assert.False(editor.CanConfirm);
        Assert.Equal(2, editor.StrengthTypes.Count);
        var view = new MaterialEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        try
        {
            var choice = view.FindControl<ComboBox>("MaterialStrengthTypeInput")!;
            Assert.Equal(Strings.MaterialStrengthType, AutomationProperties.GetName(choice));
            Assert.Null(choice.SelectedItem);
            editor.CompleteStrengthTypeInput(); Assert.True(editor.ShowStrengthTypeError);
            foreach (var type in editor.StrengthTypes)
            {
                choice.SelectedItem = type;
                foreach (var category in editor.Categories)
                {
                    editor.Category = category;
                    Assert.Equal(type.Value, editor.Draft!.StrengthType);
                    Assert.Equal(235e6, editor.Draft.YieldStrength.Pascals);
                }
                window.Measure(new Size(1100, 650)); window.Arrange(new Rect(0, 0, 1100, 650)); Dispatcher.UIThread.RunJobs();
                Assert.True(editor.CanConfirm); Assert.False(editor.ShowStrengthTypeError);
                var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), t => t.DataContext == editor.Inputs[1]);
                Assert.Equal(MaterialStrengthDisplay.Label(type.Value) + " [MPa]", AutomationProperties.GetName(input));
                var row = new MaterialSelectionItem(editor.Draft!, MaterialSelectionKind.Project);
                Assert.Equal(MaterialStrengthDisplay.Symbol(type.Value) + " 235", row.StrengthSummary);
                Assert.Contains(row.StrengthSummary, row.AccessibleName);
                main.Setup.SelectedMaterial = editor.Draft;
                Assert.Contains(row.StrengthSummary + " MPa", main.Setup.MaterialSummary);
            }
            editor.Inputs[1].Text = "0"; Assert.False(editor.CanConfirm);
            editor.Inputs[1].Text = "235";
            editor.StrengthType = new((MaterialStrengthType)999, "invalid"); Assert.False(editor.CanConfirm);
        }
        finally { window.Close(); }
    }
}

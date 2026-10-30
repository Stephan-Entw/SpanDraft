using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopLoadFlyoutControlTests
{
    [Theory]
    [InlineData("de-DE", PointLoadKind.Force)]
    [InlineData("en-US", PointLoadKind.Force)]
    [InlineData("de-DE", PointLoadKind.Moment)]
    [InlineData("en-US", PointLoadKind.Moment)]
    public void PointAndMomentButtonsPreserveTextAndUpdatePreview(string culture, PointLoadKind kind)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        editor.ToggleLoadTool(kind); editor.HoverLoadPlacement(Length.FromMeters(.5)); Assert.True(editor.PlaceLoad());
        var flyout = new PointLoadFlyout { DataContext = editor };
        var window = Open(flyout);
        var input = flyout.FindControl<TextBox>("ValueInput")!;
        var sign = flyout.FindControl<Button>("SignButton")!;
        Assert.Equal(Strings.ToggleLoadSign, Avalonia.Automation.AutomationProperties.GetName(sign));
        VerifySign(flyout, input, sign, culture, () => editor.LoadPreview!.Value);
        AssertNumericAlignment(flyout, "PositionInput", "PositionUnit");
        AssertNameAlignment(flyout, "PositionInput");
        Assert.True(editor.IsLoadFlyoutVisible);
        sign.Focus();
        sign.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsLoadFlyoutVisible);
        Assert.StartsWith("-", input.Text!);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void DistributedButtonPreservesTextAndUpdatesPreview(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(Length.FromMeters(.2)); Assert.True(editor.PlaceDistributedLoadEndpoint());
        editor.HoverDistributedLoadPlacement(Length.FromMeters(.8)); Assert.True(editor.PlaceDistributedLoadEndpoint());
        var flyout = new DistributedLoadFlyout { DataContext = editor };
        var window = Open(flyout);
        VerifySign(flyout, flyout.FindControl<TextBox>("ValueInput")!, flyout.FindControl<Button>("SignButton")!, culture,
            () => editor.DistributedLoadPreview!.Intensity);
        AssertNumericAlignment(flyout, "StartInput", "StartUnit");
        AssertNumericAlignment(flyout, "EndInput", "EndUnit");
        AssertNameAlignment(flyout, "StartInput");
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", SupportType.Fixed)]
    [InlineData("de-DE", SupportType.Pinned)]
    [InlineData("de-DE", SupportType.Roller)]
    [InlineData("en-US", SupportType.Fixed)]
    [InlineData("en-US", SupportType.Pinned)]
    [InlineData("en-US", SupportType.Roller)]
    public void EverySupportUsesTheSameFieldWidthsForNewAndExistingDrafts(string culture, SupportType type)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        editor.ToggleSupportTool(type); editor.HoverPlacement(Length.FromMeters(.5));
        Assert.True(editor.PlaceSupport());
        var flyout = new SupportFlyout { DataContext = editor };
        var window = Open(flyout);
        AssertNameAlignment(flyout, "PositionInput");
        AssertSupportTypeAlignment(flyout);
        Assert.Equal(type, editor.SupportDraft!.Type);
        var typeInput = flyout.FindControl<ComboBox>("TypeInput")!;
        var caption = Assert.Single(typeInput.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == editor.SupportDraft.SelectedType!.Name);
        Assert.True(caption.TextLayout.TextLines[0].Width <= caption.Bounds.Width,
            $"The selected support type is clipped: {caption.Text}, text {caption.TextLayout.TextLines[0].Width}, bounds {caption.Bounds.Width}, combo {typeInput.Bounds.Width}");
        Assert.True(caption.TranslatePoint(new Point(), typeInput)!.Value.X >= 8);
        var glyph = Assert.Single(typeInput.GetVisualDescendants().OfType<PathIcon>(), icon => icon.Name == "DropDownGlyph");
        Assert.True(caption.TranslatePoint(new Point(), typeInput)!.Value.X + caption.TextLayout.TextLines[0].Width
            <= glyph.TranslatePoint(new Point(), typeInput)!.Value.X);
        typeInput.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
        Assert.True(typeInput.IsDropDownOpen);
        var popup = Assert.Single(typeInput.GetVisualDescendants().OfType<Popup>());
        Assert.True(popup.IsOpen);
        Assert.Equal(3, typeInput.ItemCount);
        Assert.All(SupportDraftViewModel.Types, option => Assert.Contains(option, typeInput.Items));
        var next = SupportDraftViewModel.Types.First(option => option.Type != type);
        typeInput.SelectedItem = next; Dispatcher.UIThread.RunJobs();
        Assert.Equal(next.Type, editor.SupportDraft.Type);
        typeInput.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs();
        Assert.False(typeInput.IsDropDownOpen);
        typeInput.SelectedItem = SupportDraftViewModel.Types.First(option => option.Type == type);
        Assert.True(editor.ConfirmSupport());
        Assert.True(editor.EditSupport(editor.Document.Supports[0].Id));
        Dispatcher.UIThread.RunJobs();
        AssertNameAlignment(flyout, "PositionInput");
        AssertSupportTypeAlignment(flyout);
        Assert.Equal(type, editor.SupportDraft!.Type);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TypeDropdownInsideSupportPopupAllowsChoosingAnItem(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(SupportedDocument(), () => { });
        var view = new EditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 1250, Height = 800 };
        window.Show(); window.Measure(new Size(1250, 800)); window.Arrange(new Rect(0, 0, 1250, 800));
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.EditSupport(editor.Document.Supports[0].Id)); Dispatcher.UIThread.RunJobs();
        var surface = Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
        Assert.True(surface.FindControl<Popup>("SupportPopup")!.IsOpen);
        var flyout = surface.FindControl<SupportFlyout>("SupportEditor")!;
        var type = flyout.FindControl<ComboBox>("TypeInput")!;
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var openPoint = type.TranslatePoint(new Point(type.Bounds.Width - 8, type.Bounds.Height / 2), window)!.Value;
        type.RaiseEvent(new PointerPressedEventArgs(type, pointer, window, openPoint, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        type.RaiseEvent(new PointerReleasedEventArgs(type, pointer, window, openPoint, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        pointer.Capture(null); Dispatcher.UIThread.RunJobs();
        Assert.True(type.IsDropDownOpen);
        var item = Assert.IsType<ComboBoxItem>(type.ContainerFromIndex(1));
        var option = SupportDraftViewModel.Types[1];
        var label = Assert.Single(item.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == option.Name);
        Assert.True(label.TextLayout.TextLines[0].Width <= label.Bounds.Width,
            $"The dropdown item is clipped: {label.Text}, text {label.TextLayout.TextLines[0].Width}, bounds {label.Bounds.Width}");
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        item.RaiseEvent(new PointerPressedEventArgs(item, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Assert.True(editor.IsSupportFlyoutVisible);
        item.RaiseEvent(new PointerReleasedEventArgs(item, pointer, window, point, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        pointer.Capture(null); Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsSupportFlyoutVisible);
        Assert.Equal(SupportType.Pinned, editor.SupportDraft!.Type);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", "point")]
    [InlineData("en-US", "point")]
    [InlineData("de-DE", "moment")]
    [InlineData("en-US", "moment")]
    [InlineData("de-DE", "distributed")]
    [InlineData("en-US", "distributed")]
    [InlineData("de-DE", "support")]
    [InlineData("en-US", "support")]
    public void FooterKeepsAllActionsTogetherAtTheRightContentEdge(string culture, string kind)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(SupportedDocument(), () => { });
        Control flyout;
        if (kind is "point" or "moment")
        {
            editor.ToggleLoadTool(kind == "point" ? PointLoadKind.Force : PointLoadKind.Moment);
            editor.HoverLoadPlacement(Length.FromMeters(.5));
            Assert.True(editor.PlaceLoad()); Assert.True(editor.ConfirmLoad());
            Assert.True(editor.EditLoad(editor.Document.Loads[0].Id));
            flyout = new PointLoadFlyout { DataContext = editor };
        }
        else if (kind == "distributed")
        {
            editor.ToggleDistributedLoadTool();
            editor.HoverDistributedLoadPlacement(Length.FromMeters(.2)); Assert.True(editor.PlaceDistributedLoadEndpoint());
            editor.HoverDistributedLoadPlacement(Length.FromMeters(.8)); Assert.True(editor.PlaceDistributedLoadEndpoint());
            Assert.True(editor.ConfirmDistributedLoad()); Assert.True(editor.EditDistributedLoad(editor.Document.DistributedLoads[0].Id));
            flyout = new DistributedLoadFlyout { DataContext = editor };
        }
        else
        {
            Assert.True(editor.EditSupport(editor.Document.Supports[0].Id));
            flyout = new SupportFlyout { DataContext = editor };
        }

        var window = Open(flyout);
        var delete = flyout.FindControl<Button>("DeleteAction")!;
        var neutral = flyout.FindControl<Button>("CancelAction")!;
        var primary = flyout.FindControl<Button>("ConfirmAction")!;
        var footer = flyout.FindControl<StackPanel>("Footer")!;
        var content = (Control)footer.Parent!;
        Assert.Equal(new Control[] { delete, neutral, primary }, footer.Children);
        Assert.Contains("dangerGhost", delete.Classes);
        Assert.Contains("ghost", neutral.Classes);
        Assert.Contains("primary", primary.Classes);
        Assert.All(new[] { delete, neutral, primary }, button =>
        {
            Assert.Equal(button.Padding.Left, button.Padding.Right);
            Assert.Equal(button.Padding.Top, button.Padding.Bottom);
            Assert.Equal(default, button.Margin);
        });
        (double left, double rightGap, double middleGap, double groupGap, double width) AtWidth(double width)
        {
            flyout.Measure(new Size(width, 400)); flyout.Arrange(new Rect(0, 0, width, 400));
            Dispatcher.UIThread.RunJobs();
            var d = delete.TranslatePoint(new Point(), content)!.Value.X;
            var n = neutral.TranslatePoint(new Point(), content)!.Value.X;
            var p = primary.TranslatePoint(new Point(), content)!.Value.X;
            Assert.True(d >= 0);
            Assert.All(new[] { delete, neutral, primary }, button =>
                Assert.True(button.DesiredSize.Width <= button.Bounds.Width));
            return (d, content.Bounds.Width - p - primary.Bounds.Width, n - d - delete.Bounds.Width,
                p - n - neutral.Bounds.Width, content.Bounds.Width);
        }
        var compact = AtWidth(360); var wide = AtWidth(480);
        Assert.Equal(footer.Spacing, compact.middleGap, 3);
        Assert.Equal(footer.Spacing, compact.groupGap, 3);
        Assert.Equal(0, compact.rightGap, 3);
        Assert.Equal(0, wide.rightGap, 3);
        Assert.Equal(compact.middleGap, wide.middleGap, 3);
        Assert.Equal(compact.groupGap, wide.groupGap, 3);
        Assert.True(wide.width > compact.width);
        Assert.Equal(wide.width - compact.width, wide.left - compact.left, 3);
        if (kind == "support") Assert.InRange(flyout.FindControl<TextBox>("PositionInput")!.Bounds.Width, 149, 151);
        window.Close();
    }

    private static EditorDocument SupportedDocument() => DesktopLayoutFixture.Document().WithSupports(
        [new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Fixed, "A")]);

    private static void VerifySign(Control flyout, TextBox input, Button sign, string culture, Func<double> preview)
    {
        string decimalSeparator = culture == "de-DE" ? "," : ".";
        string value = "  +1\u202f234" + decimalSeparator + "50E+2  ";
        input.Text = value; Dispatcher.UIThread.RunJobs();
        double before = preview(); Assert.True(before > 0);
        Click(sign); Assert.Equal(value.Replace("+1", "-1"), input.Text); Assert.Equal(-before, preview());
        Click(sign); Assert.Equal(value.Replace("+1", "1"), input.Text); Assert.Equal(before, preview());
        Click(sign); Assert.Equal(value.Replace("+1", "-1"), input.Text); Assert.Equal(-before, preview());
        foreach (string unchanged in new[] { "0", "-0", "", "-", "1E+", "1" + decimalSeparator, "oops" })
        {
            input.Text = unchanged; Dispatcher.UIThread.RunJobs();
            double prior = preview(); Click(sign);
            Assert.Equal(unchanged, input.Text); Assert.Equal(prior, preview());
        }
        var error = Assert.Single(flyout.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Classes.Contains("validationMessage") && t.IsVisible);
        Assert.True(error.DesiredSize.Width <= error.Bounds.Width);
        Assert.True(error.DesiredSize.Height <= error.Bounds.Height);
        input.Text = "5"; Dispatcher.UIThread.RunJobs();
        Assert.Contains(input, sign.GetVisualAncestors());
        input.Focus(); Dispatcher.UIThread.RunJobs();
        AssertSignInsideBorder(input, sign);
        sign.Focus(); Dispatcher.UIThread.RunJobs();
        AssertSignInsideBorder(input, sign);
        var footer = flyout.FindControl<StackPanel>("Footer")!;
        var inputPoint = input.TranslatePoint(new Point(), flyout)!.Value;
        var footerPoint = footer.TranslatePoint(new Point(), flyout)!.Value;
        Assert.True(footerPoint.Y - inputPoint.Y - input.Bounds.Height > 8);
    }

    private static void AssertSignInsideBorder(TextBox input, Button sign)
    {
        var border = Assert.Single(input.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_BorderElement");
        var buttonPoint = sign.TranslatePoint(new Point(), input)!.Value;
        var borderPoint = border.TranslatePoint(new Point(), input)!.Value;
        var thickness = border.BorderThickness;
        Assert.True(buttonPoint.X >= borderPoint.X + thickness.Left + 1);
        Assert.True(buttonPoint.Y >= borderPoint.Y + thickness.Top + 1);
        Assert.True(buttonPoint.X + sign.Bounds.Width <= borderPoint.X + border.Bounds.Width - thickness.Right - 1);
        Assert.True(buttonPoint.Y + sign.Bounds.Height <= borderPoint.Y + border.Bounds.Height - thickness.Bottom - 1);
    }

    private static void AssertNumericAlignment(Control flyout, string positionName, string unitName)
    {
        var position = flyout.FindControl<TextBox>(positionName)!;
        Assert.InRange(position.Bounds.Width, 149, 151);
        var value = flyout.FindControl<TextBox>("ValueInput")!;
        var positionUnit = flyout.FindControl<TextBlock>(unitName)!;
        var valueUnit = flyout.FindControl<TextBlock>("ValueUnit")!;
        Assert.Equal(position.Bounds.Width, value.Bounds.Width, 3);
        Assert.Equal(position.TranslatePoint(new Point(), flyout)!.Value.X, value.TranslatePoint(new Point(), flyout)!.Value.X, 3);
        Assert.Equal(positionUnit.TranslatePoint(new Point(), flyout)!.Value.X, valueUnit.TranslatePoint(new Point(), flyout)!.Value.X, 3);
    }

    private static void AssertNameAlignment(Control flyout, string positionName)
    {
        var name = flyout.FindControl<TextBox>("NameInput")!;
        var position = flyout.FindControl<TextBox>(positionName)!;
        Assert.InRange(Math.Abs(name.Bounds.Width - position.Bounds.Width), 0, 1);
        Assert.InRange(Math.Abs(name.TranslatePoint(new Point(), flyout)!.Value.X - position.TranslatePoint(new Point(), flyout)!.Value.X), 0, 1);
    }

    private static void AssertSupportTypeAlignment(Control flyout)
    {
        var type = flyout.FindControl<ComboBox>("TypeInput")!;
        var position = flyout.FindControl<TextBox>("PositionInput")!;
        Assert.InRange(Math.Abs(type.Bounds.Width - position.Bounds.Width), 0, 1);
        Assert.InRange(Math.Abs(type.TranslatePoint(new Point(), flyout)!.Value.X - position.TranslatePoint(new Point(), flyout)!.Value.X), 0, 1);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Window Open(Control flyout)
    {
        var window = new Window { Content = flyout, Width = 360, Height = 400 };
        window.Show(); window.Measure(new Size(360, 400)); window.Arrange(new Rect(0, 0, 360, 400));
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}

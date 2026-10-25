using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopTextBoxValidationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SharedChromeResolvesNormalFocusErrorAndHoverStates(bool inline)
    {
        using var environment = new DesktopControlEnvironment();
        var box = new TextBox { Text = "12", Width = 150, Classes = { "numeric" } };
        if (inline) box.Classes.Add("inlineValidation");
        var next = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { box, next } } };
        window.Show(); Layout(window);
        var border = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
        next.Focus();
        foreach (bool error in new[] { false, true })
        foreach (bool focused in new[] { false, true })
        foreach (bool hover in new[] { false, true })
        {
            box.Classes.Set("error", error);
            ((IPseudoClasses)box.Classes).Set(":pointerover", hover);
            if (focused) box.Focus(); else next.Focus();
            Layout(window);
            Assert.Equal(new Thickness(focused ? 2 : 1), border.BorderThickness);
            Assert.Same(Application.Current!.FindResource(error ? "Error" : focused ? "Accent" : hover ? "TextMuted" : "BorderStrong"), border.BorderBrush);
            Assert.Same(Application.Current!.FindResource(error ? "ErrorSubtle" : !focused && hover ? "SurfaceHover" : "Surface"), border.Background);
            if (inline && error)
            {
                var icon = Assert.IsType<PathIcon>(box.InnerRightContent);
                Assert.False(icon.Focusable); Assert.False(icon.IsHitTestVisible);
                Assert.Same(Application.Current!.FindResource("FluentIcon.ErrorCircle"), icon.Data);
            }
            else Assert.Null(box.InnerRightContent);
        }
        window.Close();
    }

    [Fact]
    public void InlineContentUsesSeparateSpaceAndKeepsFocusSelectionAndTabOrder()
    {
        using var environment = new DesktopControlEnvironment();
        var box = new TextBox { Text = "12", Width = 150, Classes = { "numeric", "inlineValidation" } };
        var next = new TextBox { Width = 150, Classes = { "inlineValidation", "error" } };
        var window = new Window { Content = new StackPanel { Children = { box, next } } };
        window.Show(); Layout(window); box.Focus(); Layout(window);
        int lostFocus = 0; box.LostFocus += (_, _) => lostFocus++;
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().Single();
        var scroll = box.GetVisualDescendants().OfType<ScrollViewer>().Single();
        double normalWidth = scroll.Bounds.Width;
        Rect outerBounds = box.Bounds;
        Assert.Null(box.InnerRightContent);
        Assert.Equal(TextAlignment.Right, presenter.TextAlignment);
        Assert.True(presenter.TranslatePoint(default, scroll)!.Value.X > 0);
        Assert.Equal(scroll.Bounds.Width, presenter.TranslatePoint(new Point(presenter.Bounds.Width, 0), scroll)!.Value.X, 6);
        box.SelectionStart = 0; box.SelectionEnd = 1;
        int caret = box.CaretIndex;
        box.Classes.Add("error"); Layout(window);
        var icon = Assert.IsType<PathIcon>(box.InnerRightContent);
        Assert.NotSame(icon, next.InnerRightContent);
        Assert.True(box.IsFocused); Assert.Equal(0, lostFocus);
        Assert.Equal(0, box.SelectionStart); Assert.Equal(1, box.SelectionEnd); Assert.Equal(caret, box.CaretIndex);
        Assert.Equal(outerBounds, box.Bounds);
        var iconBounds = new Rect(icon.TranslatePoint(default, box)!.Value, icon.Bounds.Size);
        var textBounds = new Rect(scroll.TranslatePoint(default, box)!.Value, scroll.Bounds.Size);
        Assert.True(new Rect(box.Bounds.Size).Contains(iconBounds));
        Assert.True(textBounds.Right <= iconBounds.Left);
        Assert.Equal(normalWidth - icon.Bounds.Width - icon.Margin.Left - icon.Margin.Right, scroll.Bounds.Width, 6);
        ClickIcon(); Assert.True(box.IsFocused); Assert.Equal(0, lostFocus);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, box, NavigationDirection.Next)); Assert.True(next.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, next, NavigationDirection.Previous)); Assert.True(box.IsFocused);
        next.Focus(); int previousLosses = lostFocus;
        ClickIcon(); Assert.True(box.IsFocused); Assert.Equal(previousLosses, lostFocus);
        box.SelectionStart = 0; box.SelectionEnd = 1; caret = box.CaretIndex;
        box.Classes.Remove("error"); Layout(window);
        Assert.Null(box.InnerRightContent); Assert.Equal(normalWidth, scroll.Bounds.Width);
        Assert.True(box.IsFocused); Assert.Equal(0, box.SelectionStart); Assert.Equal(1, box.SelectionEnd); Assert.Equal(caret, box.CaretIndex);
        Assert.Same(presenter, box.GetVisualDescendants().OfType<TextPresenter>().Single());
        window.Close();

        void ClickIcon()
        {
            var point = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), window)!.Value;
            Paint(window);
            var hit = Assert.IsAssignableFrom<Control>(window.InputHitTest(point));
            Assert.True(ReferenceEquals(hit, box) || hit.GetVisualAncestors().Contains(box));
            Assert.False(ReferenceEquals(hit, icon));
            var pointer = new Pointer(72, PointerType.Mouse, true);
            hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, window, point, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            hit.RaiseEvent(new PointerReleasedEventArgs(hit, pointer, window, point, 1,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            pointer.Capture(null); Layout(window);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EditingKeysKeepNativeBehaviorWithAndWithoutInlineError(bool error)
    {
        using var environment = new DesktopControlEnvironment();
        var box = new TextBox { Text = "1234", Width = 150, Classes = { "numeric", "inlineValidation" } };
        box.Classes.Set("error", error);
        var window = new Window { Content = box }; window.Show(); Layout(window); box.Focus();
        box.ClearSelection(); box.CaretIndex = 2;
        Type("5"); Assert.Equal("12534", box.Text); Assert.Equal(3, box.CaretIndex);
        Press(Key.Left); Assert.Equal(2, box.CaretIndex);
        Press(Key.Back); Assert.Equal("1534", box.Text); Assert.Equal(1, box.CaretIndex);
        Press(Key.Delete); Assert.Equal("134", box.Text); Assert.Equal(1, box.CaretIndex);
        Press(Key.Home); Assert.Equal(0, box.CaretIndex);
        Press(Key.End); Assert.Equal(box.Text!.Length, box.CaretIndex);
        Press(Key.Left, KeyModifiers.Shift); Assert.NotEqual(box.SelectionStart, box.SelectionEnd);
        Type("6"); Assert.Equal("136", box.Text);
        box.SelectAll(); Type("-"); Assert.Equal("-", box.Text); Assert.True(box.IsFocused);
        box.SelectAll(); Type(new string('1', 60)); Press(Key.End); Layout(window);
        var scroll = box.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().Single();
        var caret = presenter.TextLayout.HitTestTextPosition(box.CaretIndex);
        var visibleCaret = presenter.TranslatePoint(caret.Position, scroll)!.Value;
        Assert.InRange(visibleCaret.X, 0, scroll.Bounds.Width + 1);
        Press(Key.Home); Layout(window);
        Assert.Equal(0, box.CaretIndex);
        Assert.InRange(presenter.TranslatePoint(presenter.TextLayout.HitTestTextPosition(0).Position, scroll)!.Value.X, -1, scroll.Bounds.Width);

        window.Close();
        void Type(string text) { box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text }); Layout(window); }
        void Press(Key key, KeyModifiers modifiers = KeyModifiers.None) => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    }

    [Fact]
    public void ClickingSectionErrorDoesNotCommitPendingText()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new SectionDefinitionViewModel(); model.SelectForm(SectionEditorType.Rectangle);
        var view = new SectionDefinitionView { DataContext = model };
        var window = new Window { Content = view }; window.Show(); Layout(window);
        var box = view.GetVisualDescendants().OfType<TextBox>().First(b => b.DataContext is SectionDefinitionInput);
        var input = Assert.IsType<SectionDefinitionInput>(box.DataContext);
        box.Focus(); box.Text = "invalid"; input.Commit(); Layout(window);
        var icon = Assert.IsType<PathIcon>(box.InnerRightContent);
        Assert.Equal(input.Error, ToolTip.GetTip(box)); Assert.Equal(input.Error, AutomationProperties.GetHelpText(box));
        box.Text = "12"; Layout(window); Assert.True(input.IsPending); Assert.Null(input.ConfirmedValue);
        int losses = 0; box.LostFocus += (_, _) => losses++;
        Paint(window);
        var point = icon.TranslatePoint(new Point(8, 8), window)!.Value;
        var hit = Assert.IsAssignableFrom<Control>(window.InputHitTest(point));
        var pointer = new Pointer(73, PointerType.Mouse, true);
        hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        pointer.Capture(null); Layout(window);
        Assert.True(box.IsFocused); Assert.Equal(0, losses); Assert.True(input.IsPending); Assert.Null(input.ConfirmedValue);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Layout(window);
        Assert.NotNull(input.ConfirmedValue); Assert.False(input.HasError); Assert.Null(box.InnerRightContent);
        window.Close();
    }

    [Fact]
    public void LengthInputKeepsItsErrorInsideTheFieldAndExposesTheMessage()
    {
        using var environment = new DesktopControlEnvironment();
        var model = new LengthInputViewModel(() => Length.FromMeters(1), _ => LengthCommitResult.Success);
        var view = new LengthInput { DataContext = model };
        var window = new Window { Content = view }; window.Show(); Layout(window);
        var box = view.FindControl<TextBox>("InputBox")!;
        box.Focus(); box.Text = "-";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Layout(window);
        Assert.True(model.HasError); Assert.True(model.IsEditing); Assert.True(box.IsFocused);
        Assert.IsType<PathIcon>(box.InnerRightContent);
        Assert.Equal(model.ErrorText, ToolTip.GetTip(box)); Assert.Equal(model.ErrorText, AutomationProperties.GetHelpText(box));
        Assert.Equal(TextAlignment.Right, box.TextAlignment);
        box.Text = "12"; Layout(window);
        Assert.False(model.HasError); Assert.Null(box.InnerRightContent); Assert.True(model.IsEditing);
        window.Close();
    }

    private static void Paint(Window window)
    {
        // The inert timer never renders; paint once to populate Avalonia's real hit-test tree.
        var source = window.GetPresentationSource()!;
        var renderer = source.GetType().GetProperty("Renderer")!.GetValue(source)!;
        renderer.GetType().GetMethod("Paint", [typeof(Rect), typeof(bool)])!.Invoke(renderer, [new Rect(window.ClientSize), false]);
    }

    private static void Layout(Window window)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs(); window.Measure(new Size(1100, 650)); window.Arrange(new Rect(0, 0, 1100, 650));
            var content = (Control)window.Content!; content.Measure(new Size(1100, 650)); content.Arrange(new Rect(0, 0, 1100, 650));
        }
    }
}

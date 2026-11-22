using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Reporting.Notation;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopSettingsControlTests
{
    /// <summary>The default window size, and the smallest the window supports.</summary>
    private static (double Width, double Height) Size(bool smallest)
    {
        var probe = new SettingsWindow(new(UserSettings.Default, _ => Task.FromResult<string?>(null)));
        return smallest ? (probe.MinWidth, probe.MinHeight) : (probe.Width, probe.Height);
    }

    [Theory]
    [InlineData("de-DE", false)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", true)]
    [InlineData("en-US", true)]
    public void DialogKeepsAFixedFooterBelowOneScrollablePageWithAlignedForms(string culture, bool smallest)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var (width, height) = Size(smallest);
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        var window = new SettingsWindow(model);
        Arrange(window, width, height);
        var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
        Assert.Null(window.FindControl<TextBox>("DefaultAuthorInput"));
        var headings = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("sectionTitle"));
        Assert.Equal(Strings.SettingsUnits, headings.First().Text);
        var language = window.FindControl<ComboBox>("LanguagePicker")!;
        var region = window.FindControl<ComboBox>("RegionalFormatPicker")!;
        Assert.True(language.IsEnabled); Assert.True(region.IsEnabled);
        Assert.Equal(3, language.Items.Count); Assert.Equal(3, region.Items.Count);
        Assert.Equal(UiLanguagePreference.System, language.SelectedValue);
        Assert.Equal(RegionalFormatPreference.System, region.SelectedValue);
        var restart = window.FindControl<TextBlock>("LocaleRestartHint")!;
        Assert.Equal(Strings.LocaleRestartHint, restart.Text);
        Assert.True(restart.Bounds.Height >= restart.TextLayout.Height);
        Assert.Null(window.FindControl<Button>("ApplyButton"));
        var ok = window.FindControl<Button>("OkButton")!;
        var cancel = window.FindControl<Button>("CancelButton")!;
        Assert.True(ok.IsEnabled);
        Assert.Equal(Strings.OK, ok.Content); Assert.Equal(Strings.Cancel, cancel.Content);
        Assert.Equal(ok.Bounds.Height, cancel.Bounds.Height);
        Assert.Equal(ok.Bounds.Top, cancel.Bounds.Top);
        Assert.Equal(ok.Bounds.Width, cancel.Bounds.Width, 3);
        Assert.Contains("primary", ok.Classes); Assert.Contains("secondary", cancel.Classes);
        Assert.True(ok.IsDefault); Assert.False(cancel.IsDefault); Assert.True(cancel.IsCancel); Assert.False(ok.IsCancel);
        AssertFooterIsFixed(window, scroll, width, height);
        Assert.False(window.FindControl<RadioButton>("CustomProfileOption")!.IsVisible);
        var profiles = window.FindControl<StackPanel>("ProfileRow")!;
        var radios = profiles.Children.OfType<RadioButton>().Where(r => r.IsVisible).ToArray();
        Assert.Equal(3, radios.Length);
        Assert.True(radios[1].Bounds.Left >= radios[0].Bounds.Right);
        Assert.True(radios[2].Bounds.Left >= radios[1].Bounds.Right);
        Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());
        // Units are grouped by topic in two columns: geometry and loads left, the rest right; each group stands under
        // its heading, in the order of the product decision.
        var columns = new[] { window.FindControl<ItemsControl>("UnitGroupListLeft")!, window.FindControl<ItemsControl>("UnitGroupListRight")! };
        var groupsPerColumn = new[] { model.UnitGroups.Take(2).ToArray(), model.UnitGroups.Skip(2).ToArray() };
        Assert.Equal(new[] { 2, 3 }, groupsPerColumn.Select(g => g.Length));
        Assert.True(X(columns[1], window) > X(columns[0], window) + columns[0].Bounds.Width - .01, "the columns do not overlap");
        Assert.Equal(Y(columns[0], window), Y(columns[1], window), 3);
        foreach (var (list, groups) in columns.Zip(groupsPerColumn))
        {
            var groupTitles = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("bodyStrong")).ToArray();
            Assert.Equal(groups.Select(g => g.Title), groupTitles.Select(t => t.Text));
            Assert.Equal(groupTitles.OrderBy(t => Y(t, window)), groupTitles);
            foreach (var row in groups.SelectMany((g, i) => g.Units.Select(u => (Group: i, Unit: u))))
            {
                var label = list.GetVisualDescendants().OfType<TextBlock>().Single(t => TechnicalTextPresenter.GetContent(t) is not null && ReferenceEquals(t.DataContext, row.Unit));
                Assert.True(Y(label, window) > Y(groupTitles[row.Group], window), $"{row.Unit.Label} stands under its heading");
                if (row.Group + 1 < groupTitles.Length) Assert.True(Y(label, window) < Y(groupTitles[row.Group + 1], window), $"{row.Unit.Label} stands above the next heading");
                var symbol = row.Unit.Quantity switch
                {
                    QuantityKind.Area => TechnicalSymbols.Area,
                    QuantityKind.SecondMomentOfArea => TechnicalSymbols.Inertia(),
                    QuantityKind.SectionModulus => TechnicalSymbols.Modulus(),
                    _ => null
                };
                Assert.Equal(row.Unit.Label, new TextBlockAutomationPeer(label).GetName());
                if (row.Unit.IsEditable)
                    Assert.Equal(row.Unit.Label, AutomationProperties.GetName(((Grid)label.Parent!).Children.OfType<ComboBox>().Single()));
                var runs = label.Inlines!.Cast<Run>().ToArray();
                if (symbol is not null)
                {
                    Assert.Equal(symbol.Runs, row.Unit.LabelText.Runs.TakeLast(symbol.Runs.Count));
                    Assert.EndsWith(" " + symbol.PlainText, row.Unit.Label);
                    Assert.Equal(FontStyle.Italic, runs[^1].FontStyle);
                    Assert.Equal(FontStyle.Normal, runs[0].FontStyle);
                }
                else Assert.All(runs, run => Assert.Equal(FontStyle.Normal, run.FontStyle));
                Assert.True(label.TextLayout.Width <= label.Bounds.Width + .01, $"label clipped: {row.Unit.Label}");
                Assert.True(label.TextLayout.Height <= label.Bounds.Height + .01, $"label clipped: {row.Unit.Label}");
            }
        }
        Assert.Equal(16, columns.SelectMany(c => c.GetVisualDescendants().OfType<ComboBox>()).Count(c => c.IsVisible));
        AssertUnitsFitTheWindowWidth(window, scroll);
        // Switching the unit system must show every changed field at once: at the default size the profile choice
        // and all unit fields are inside the viewport without scrolling.
        if (!smallest) AssertUnitsAreVisibleWithoutScrolling(window, scroll);
        AssertOneFormRaster(window);
        AssertNumericSection(window);
        Export(window, $"settings-{culture}-{width}x{height}-top.png", width, height);
        scroll.Offset = new(0, window.FindControl<Grid>("NumericSettings")!.TranslatePoint(default, (Control)scroll.Content!)!.Value.Y - 60);
        Arrange(window, width, height);
        AssertFooterIsFixed(window, scroll, width, height);
        Export(window, $"settings-{culture}-{width}x{height}-numeric.png", width, height);
        scroll.Offset = default;
        Arrange(window, width, height);
        model.ChangeUnit(QuantityKind.BeamLength, UnitCatalog.Inch);
        Arrange(window, width, height);
        Assert.True(window.FindControl<RadioButton>("CustomProfileOption")!.IsVisible);
        Assert.True(window.FindControl<Button>("OkButton")!.IsEnabled);
        if (smallest) Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the content scrolls instead of growing the window");
        Export(window, $"settings-{culture}-{width}x{height}-custom.png", width, height);
    }

    /// <summary>
    /// A rejected save says so next to the buttons that triggered it, not at the end of the scrolled form: the message stays
    /// visible and inside the window, while the form above it scrolls.
    /// </summary>
    [Theory]
    [InlineData("de-DE", false)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", true)]
    [InlineData("en-US", true)]
    public async Task ARejectedSaveShowsItsMessageInTheFixedFooter(string culture, bool smallest)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var (width, height) = Size(smallest);
        const string message = "Die Einstellungen konnten nicht gespeichert werden: Zugriff verweigert. Bitte Schreibrechte prüfen und erneut versuchen.";
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(message));
        var window = new SettingsWindow(model);
        Arrange(window, width, height);
        var error = window.FindControl<TextBlock>("SaveError")!;
        Assert.False(error.IsVisible);
        model.SignificantDigits = 5;
        Assert.False(await model.ApplyAsync());
        Arrange(window, width, height);
        var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
        Assert.True(error.IsVisible); Assert.Equal(message, error.Text);
        Assert.DoesNotContain(error.GetVisualAncestors(), a => ReferenceEquals(a, scroll));
        var bounds = new Rect(error.TranslatePoint(default, window)!.Value, error.Bounds.Size);
        Assert.True(bounds.Bottom <= height + .5 && bounds.Right <= width + .5 && bounds.X >= 0, "the message lies inside the window");
        Assert.True(error.TextLayout.Height <= error.Bounds.Height + .01, "the message is not clipped");
        // The message and the buttons share the footer, and the message does not run into them.
        var ok = window.FindControl<Button>("OkButton")!; var cancel = window.FindControl<Button>("CancelButton")!;
        Assert.True(bounds.Right <= cancel.TranslatePoint(default, window)!.Value.X + .5);
        Assert.Same(window.FindControl<Border>("SettingsFooter"), error.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("dialogFooter")));
        AssertFooterIsFixed(window, scroll, width, height);
        scroll.Offset = new(0, scroll.Extent.Height); Arrange(window, width, height);
        Assert.True(error.IsVisible);
        Assert.True(error.TranslatePoint(default, window)!.Value.Y + error.Bounds.Height <= height + .5);
        Export(window, $"settings-{culture}-{width}x{height}-error.png", width, height);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void EveryUnitSymbolFitsTheNarrowUnitField(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        var window = new SettingsWindow(model);
        var (width, height) = Size(false);
        Arrange(window, width, height);
        var rows = model.UnitGroups.SelectMany(g => g.Units).Where(u => u.IsEditable).ToArray();
        for (int round = 0; round < rows.Max(r => r.Units.Count); round++)
        {
            foreach (var row in rows) row.SelectedUnit = row.Units[Math.Min(round, row.Units.Count - 1)];
            Arrange(window, width, height);
            foreach (var field in window.FindControl<Grid>("UnitColumns")!.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible))
            {
                var text = field.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsVisible && t.Text == ((UnitDefinition)field.SelectedItem!).Symbol);
                Assert.True(text.Bounds.Width >= text.TextLayout.Width - .01, $"{text.Text} fits its field");
                var arrow = field.GetVisualDescendants().OfType<PathIcon>().First(p => p.Name == "DropDownGlyph");
                var textRight = text.TranslatePoint(default, field)!.Value.X + text.TextLayout.Width;
                Assert.True(textRight <= arrow.TranslatePoint(default, field)!.Value.X, $"{text.Text} stays left of the dropdown arrow");
            }
        }
    }

    /// <summary>The footer is a row of the window below the scroll viewer, fully inside the window at any scroll position.</summary>
    private static void AssertFooterIsFixed(SettingsWindow window, ScrollViewer scroll, double width, double height)
    {
        var footer = window.FindControl<Border>("SettingsFooter")!;
        Assert.Contains("dialogFooter", footer.Classes);
        foreach (var name in new[] { "OkButton", "CancelButton" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.DoesNotContain(button.GetVisualAncestors(), a => ReferenceEquals(a, scroll));
            Assert.Contains(footer, button.GetVisualAncestors());
            var bounds = new Rect(button.TranslatePoint(default, window)!.Value, button.Bounds.Size);
            Assert.True(bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= width + .5 && bounds.Bottom <= height + .5, $"{name} lies inside the window");
        }
        var footerBounds = new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size);
        var scrollBounds = new Rect(scroll.TranslatePoint(default, window)!.Value, scroll.Bounds.Size);
        Assert.True(footerBounds.Top >= scrollBounds.Bottom - .5, "the footer does not overlap the scrolled content");
        Assert.True(footerBounds.Bottom <= height + .5);
    }

    private static double Y(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;
    private static double X(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;

    /// <summary>The radio buttons and every unit field lie completely inside the visible part of the scroll viewer.</summary>
    private static void AssertUnitsAreVisibleWithoutScrolling(SettingsWindow window, ScrollViewer scroll)
    {
        Assert.Equal(0, scroll.Offset.Y);
        var viewport = new Rect(default, scroll.Viewport);
        var radios = window.FindControl<StackPanel>("ProfileRow")!.Children.OfType<RadioButton>().Where(r => r.IsVisible).ToArray();
        var fields = window.FindControl<Grid>("UnitColumns")!.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsVisible && (c is ComboBox || (c is TextBlock t && t.Classes.Contains("propertyLabel")))).ToArray();
        Assert.Equal(16 + 18, fields.Length);
        foreach (var control in radios.Cast<Control>().Concat(fields))
        {
            var bounds = new Rect(control.TranslatePoint(default, scroll)!.Value, control.Bounds.Size);
            Assert.True(viewport.Contains(bounds.TopLeft) && viewport.Contains(bounds.BottomRight) , $"{control.GetType().Name} {AutomationProperties.GetName(control)} lies in the viewport: {bounds} in {viewport}");
        }
    }

    /// <summary>At no supported size the unit columns are cut off at the right edge.</summary>
    private static void AssertUnitsFitTheWindowWidth(SettingsWindow window, ScrollViewer scroll)
    {
        var columns = window.FindControl<Grid>("UnitColumns")!;
        Assert.True(X(columns, scroll) + columns.Bounds.Width <= scroll.Viewport.Width - 24 + .5, "the right unit column is not clipped and keeps the page margin");
        Assert.True(columns.DesiredSize.Width <= columns.Bounds.Width + .01);
    }

    /// <summary>
    /// The unit fields form one raster in both columns: the same label width, the same field width and the same distance
    /// between label and field. The number and language fields have their own wider field for words but share one
    /// label column and one left edge. The label colour and the label-field distance are those of the material editor.
    /// </summary>
    private static void AssertOneFormRaster(SettingsWindow window)
    {
        var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
        var content = (Control)scroll.Content!;
        double Xc(Control control) => control.TranslatePoint(default, content)!.Value.X;
        var sides = new[] { "UnitGroupListLeft", "UnitGroupListRight" }.Select(name => window.FindControl<ItemsControl>(name)!).ToArray();
        var unitFields = sides.Select(side => side.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToArray()).ToArray();
        var unitLabels = sides.Select(side => side.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && t.Classes.Contains("propertyLabel")).ToArray()).ToArray();
        Assert.Equal(new[] { 9, 9 }, unitLabels.Select(l => l.Length));
        var allFields = unitFields.SelectMany(f => f).ToArray();
        Assert.Equal(16, allFields.Length);
        Assert.All(allFields, f => Assert.Equal(allFields[0].Bounds.Width, f.Bounds.Width, 3));
        Assert.True(allFields[0].Bounds.Width < 160, "the unit fields are only as wide as a unit symbol needs");
        for (int side = 0; side < 2; side++)
        {
            Assert.All(unitFields[side], f => Assert.Equal(Xc(unitFields[side][0]), Xc(f), 3));
            Assert.All(unitLabels[side], l => Assert.Equal(Xc(unitLabels[side][0]), Xc(l), 3));
        }
        // Both columns have the same label width and the same distance between label and field.
        Assert.Equal(Xc(unitFields[0][0]) - Xc(unitLabels[0][0]), Xc(unitFields[1][0]) - Xc(unitLabels[1][0]), 3);
        var spacing = ((Grid)unitLabels[0][0].Parent!).ColumnSpacing;
        Assert.Equal(12, spacing);
        // The lower sections: one label column and one field width for digits, language and format.
        var lowerFields = new Control[] { window.FindControl<DiscreteScaleSlider>("RoundingScale")!,
            window.FindControl<ComboBox>("LanguagePicker")!, window.FindControl<ComboBox>("RegionalFormatPicker")! };
        Assert.All(lowerFields, f => Assert.Equal(Xc(lowerFields[0]), Xc(f), 3));
        Assert.All(lowerFields, f => Assert.Equal(lowerFields[0].Bounds.Width, f.Bounds.Width, 3));
        var lowerLabels = window.FindControl<StackPanel>("FormSections")!.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsVisible && t.Classes.Contains("propertyLabel") && t.FindAncestorOfType<ItemsControl>()?.Name != "NumberPreviewRows").ToArray();
        Assert.Equal(4, lowerLabels.Length);
        Assert.All(lowerLabels, l => Assert.Equal(Xc(lowerLabels[0]), Xc(l), 3));
        foreach (var field in lowerFields.OfType<ComboBox>()) // the displayed words are not cut off by the field
        {
            var item = field.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsVisible && t.Text == (field.SelectedItem as SetupChoiceLabel)?.Label);
            Assert.True(item.Bounds.Width >= item.TextLayout.Width - .01, $"{item.Text} is not clipped");
        }
        foreach (var label in unitLabels.SelectMany(l => l).Concat(lowerLabels))
            Assert.True(label.TextLayout.Width <= label.Bounds.Width + .01, $"label clipped: {label.Text}");
    }

    [Theory]
    [InlineData("de-DE", "Rundung", "grob", "fein", "Zehnerpotenzen bei sehr kleinen und großen Werten")]
    [InlineData("en-US", "Rounding", "coarse", "fine", "Powers of ten for very small and large values")]
    public void NumericControlsBindBothDirectionsAndRestoreDiscardedPreferences(string culture, string roundingLabel, string startLabel, string endLabel, string notationLabel)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        var window = new SettingsWindow(model); Arrange(window, 900, 780);
        var scale = window.FindControl<DiscreteScaleSlider>("RoundingScale")!;
        var powers = window.FindControl<CheckBox>("PowersOfTenOption")!;
        var slider = scale.GetVisualDescendants().OfType<Slider>().Single();
        Assert.Equal(roundingLabel, AutomationProperties.GetName(slider));
        Assert.Equal($"1 {startLabel}, 5 {endLabel}", AutomationProperties.GetHelpText(slider));
        Assert.Contains(window.FindControl<Grid>("NumericSettings")!.Children.OfType<TextBlock>(), t => t.Text == roundingLabel);
        Assert.Equal(startLabel, scale.StartLabel);
        Assert.Equal(endLabel, scale.EndLabel);
        var start = scale.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == startLabel);
        var end = scale.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == endLabel);
        Assert.True(start.TranslatePoint(default, scale)!.Value.X < end.TranslatePoint(default, scale)!.Value.X);
        Assert.Equal(notationLabel, AutomationProperties.GetName(powers));
        Assert.Equal(model.PowersOfTenHint, AutomationProperties.GetHelpText(powers));
        Assert.Equal(model.PowersOfTenHint, window.FindControl<TextBlock>("PowersOfTenHint")!.Text);
        Assert.Equal(3, slider.Value);
        Assert.True(powers.IsChecked);
        Assert.True(slider.IsSnapToTickEnabled);
        var rows = window.FindControl<ItemsControl>("NumberPreviewRows")!;
        string Displayed(int index) => rows.GetVisualDescendants().OfType<Grid>()
            .Where(g => g.DataContext is NumberPreviewRow).ElementAt(index).Children.OfType<TextBlock>().Last().Text!;
        Assert.Equal("420\u00a0·\u00a010⁻⁶ mm", Displayed(2));
        var originalPreview = Displayed(1);
        PressScaleAt(2.4);
        Assert.Equal(2, slider.Value);
        Assert.Equal(2, model.Pending.SignificantDigits);
        PressScaleAt(2.6);
        Assert.Equal(3, slider.Value);

        slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
        for (int value = 1; value <= 5; value++)
        {
            Assert.Equal(value, slider.Value);
            Assert.Equal(value, model.Pending.SignificantDigits);
            if (value < 5)
                slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
        }
        Arrange(window, 900, 780);
        Assert.NotEqual(originalPreview, Displayed(1));
        Assert.Equal(culture == "de-DE" ? "2,4583 mm" : "2.4583 mm", Displayed(1));
        powers.IsChecked = false;
        Assert.Equal(EngineeringNotation.Off, model.Pending.Notation);
        Arrange(window, 900, 780);
        Assert.Equal(culture == "de-DE" ? "0,00042 mm" : "0.00042 mm", Displayed(2));
        model.SignificantDigits = 4;
        Assert.Equal(4, slider.Value);
        model.UsesPowersOfTen = true;
        Assert.True(powers.IsChecked);
        model.DiscardPending();
        Assert.Equal(3, slider.Value);
        Assert.True(powers.IsChecked);

        void PressScaleAt(double value)
        {
            var track = slider.GetVisualDescendants().OfType<Track>().Single();
            var thumb = track.Thumb!;
            var x = thumb.Bounds.Width / 2 + (track.Bounds.Width - thumb.Bounds.Width) * (value - 1) / 4;
            var position = track.TranslatePoint(new Point(x, track.Bounds.Height / 2), window)!.Value;
            var pointer = new Pointer(1, PointerType.Mouse, true);
            var hit = value < slider.Value ? track.DecreaseButton! : track.IncreaseButton!;
            hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, window, position, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            hit.RaiseEvent(new PointerReleasedEventArgs(hit, pointer, window, position, 1,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        }
        Assert.Equal(EngineeringNotation.Automatic, model.Pending.Notation);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<RadioButton>(), r => r.GroupName == "Presentation");
    }

    [Fact]
    public void ScaleLabelsStayCenteredOnTicksWhenResized()
    {
        using var environment = new DesktopControlEnvironment();
        var scale = new DiscreteScaleSlider { Minimum = 1, Maximum = 5, Value = 3, StartLabel = "coarse", EndLabel = "fine" };
        var window = new Window { Content = scale };
        foreach (double width in new[] { 181d, 263d })
        {
            Arrange(window, width, 160);
            var slider = scale.GetVisualDescendants().OfType<Slider>().Single();
            var ticks = slider.GetVisualDescendants().OfType<TickBar>().Single(bar => bar.IsVisible && bar.Placement == TickBarPlacement.Bottom);
            var labels = scale.GetVisualDescendants().OfType<TextBlock>().Where(label => int.TryParse(label.Text, out _)).ToArray();
            Assert.Equal(new[] { "1", "2", "3", "4", "5" }, labels.Select(label => label.Text));
            for (int i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                double tickX = ticks.ReservedSpace.Width / 2 + i * (ticks.Bounds.Width - ticks.ReservedSpace.Width) / 4;
                double center = label.TranslatePoint(new Point(label.Bounds.Width / 2, 0), ticks)!.Value.X;
                Assert.Equal(tickX, center, 3);
                Assert.True(label.TextLayout.Width <= label.Bounds.Width + .01);
            }
        }
    }

    [Fact]
    public void LocalePickersBindTypedValuesInBothDirectionsAndRestoreDiscardedSelections()
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope("de-DE", "en-US");
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        var window = new SettingsWindow(model); Arrange(window, 900, 780);
        var language = window.FindControl<ComboBox>("LanguagePicker")!;
        var region = window.FindControl<ComboBox>("RegionalFormatPicker")!;
        language.SelectedItem = model.UiLanguages.Single(o => o.Value == UiLanguagePreference.English);
        region.SelectedItem = model.RegionalFormats.Single(o => o.Value == RegionalFormatPreference.GermanGermany);
        Assert.Equal(UiLanguagePreference.English, model.SelectedUiLanguage);
        Assert.Equal(RegionalFormatPreference.GermanGermany, model.SelectedRegionalFormat);
        Assert.True(model.HasChanges); Assert.Same(UnitProfile.Default, model.Pending.Profile);
        model.SelectedUiLanguage = UiLanguagePreference.German;
        model.SelectedRegionalFormat = RegionalFormatPreference.EnglishUnitedStates;
        Assert.Equal(UiLanguagePreference.German, language.SelectedValue);
        Assert.Equal(RegionalFormatPreference.EnglishUnitedStates, region.SelectedValue);
        model.DiscardPending();
        Assert.Equal(UiLanguagePreference.System, language.SelectedValue);
        Assert.Equal(RegionalFormatPreference.System, region.SelectedValue);
        Assert.False(model.HasChanges);
        Assert.Equal(new[] { "System language", "Deutsch", "English" }, model.UiLanguages.Select(o => o.Label));
        Assert.Equal(new[] { "System setting", "German (Germany)", "English (United States)" }, model.RegionalFormats.Select(o => o.Label));
    }

    [Fact]
    public void OkSavesChangesAndClosesTheWindow()
    {
        using var environment = new DesktopControlEnvironment();
        SettingsPatch? published = null;
        var model = new SettingsViewModel(UserSettings.Default, settings =>
        { published = settings; return Task.FromResult<string?>(null); });
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.SignificantDigits = 5;
        model.SelectedUiLanguage = UiLanguagePreference.English;
        model.SelectedRegionalFormat = RegionalFormatPreference.GermanGermany;
        Click(window, "OkButton");
        Assert.True(closeApproved);
        Assert.Equal(5, published!.SignificantDigits);
        Assert.Equal(UiLanguagePreference.English, published.UiLanguage);
        Assert.Equal(RegionalFormatPreference.GermanGermany, published.RegionalFormat);
        Assert.Equal(5, model.SignificantDigits); Assert.False(model.HasChanges);
    }

    [Fact]
    public void UnchangedOkClosesWithoutSaving()
    {
        using var environment = new DesktopControlEnvironment();
        int writes = 0;
        var model = new SettingsViewModel(UserSettings.Default, _ =>
        { writes++; return Task.FromResult<string?>(null); });
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        Click(window, "OkButton");
        Assert.True(closeApproved); Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("close")]
    public void CancelEscapeAndCloseDiscardPendingChangesWithoutSaving(string action)
    {
        using var environment = new DesktopControlEnvironment();
        int writes = 0;
        var model = new SettingsViewModel(UserSettings.Default, _ =>
        { writes++; return Task.FromResult<string?>(null); });
        var owner = new Window(); owner.Show();
        var window = new SettingsWindow(model); window.Show(owner);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.Select(UnitProfileKind.UnitedStates); model.SignificantDigits = 5;
        model.SelectedUiLanguage = UiLanguagePreference.English;
        model.SelectedRegionalFormat = RegionalFormatPreference.GermanGermany;
        if (action == "cancel") Click(window, "CancelButton");
        else if (action == "escape") Escape(window);
        else window.Close();
        Assert.True(closeApproved); Assert.Equal(0, writes);
        Assert.True(model.Pending.ContentEquals(UserSettings.Default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedOkKeepsWindowAndPendingChangesForCorrectionOrCancel(bool activeDraft)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new ProjectTestSupport.Files();
        if (!activeDraft) files.FailWritePath = "settings";
        var main = new MainWindowViewModel(files: files, services: TestServices.Create(settingsStore: new SpanDraft.Desktop.Persistence.LocalSettingsStore(files, "settings"))); main.NewCommand.Execute(null);
        if (activeDraft)
        {
            ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
            main.Editor!.DimensionLength.Begin(); main.Editor.DimensionLength.Text = "1200";
        }
        var applied = main.Settings;
        var model = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        var window = new SettingsWindow(model);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.Select(UnitProfileKind.UnitedStates);
        model.SelectedUiLanguage = UiLanguagePreference.English;
        model.SelectedRegionalFormat = RegionalFormatPreference.GermanGermany;
        Click(window, "OkButton");
        Assert.False(closeApproved); Assert.True(model.IsUnitedStates); Assert.True(model.HasError);
        Assert.Equal(UiLanguagePreference.English, model.SelectedUiLanguage);
        Assert.Equal(RegionalFormatPreference.GermanGermany, model.SelectedRegionalFormat);
        Assert.Same(applied, main.Settings);
        if (activeDraft) main.Editor!.DimensionLength.Cancel();
        else files.FailWritePath = null;
        Click(window, "OkButton");
        Assert.True(closeApproved); Assert.True(main.Settings.ContentEquals(model.Pending));
    }

    [Fact]
    public void SavingRejectsRepeatedOkCancelEscapeAndCloseThenClosesOnSuccess()
    {
        using var environment = new DesktopControlEnvironment();
        var completion = new TaskCompletionSource<string?>();
        int writes = 0;
        var model = new SettingsViewModel(UserSettings.Default, _ => { writes++; return completion.Task; });
        var owner = new Window(); owner.Show();
        var window = new SettingsWindow(model); window.Show(owner);
        bool closeApproved = false; window.Closing += (_, e) => closeApproved = !e.Cancel;
        model.SignificantDigits = 5;
        Click(window, "OkButton");
        Assert.True(model.IsSaving);
        Assert.False(window.FindControl<Button>("OkButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("CancelButton")!.IsEnabled);
        Click(window, "OkButton"); Click(window, "CancelButton"); Escape(window); window.Close();
        Assert.False(closeApproved); Assert.Equal(5, model.SignificantDigits); Assert.Equal(1, writes);
        completion.SetResult(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closeApproved); Assert.False(model.IsSaving); Assert.False(model.HasChanges);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void UnitChangesRefreshTextsWithoutChangingStationsGlyphAnchorsOrDiagramSources(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, culture);
        int analyses = 0;
        var document = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Fixed, "A")],
            [new EditorPointForce(Guid.NewGuid(), Length.FromMeters(.4), Force.FromNewtons(-1000), "F1"),
             new EditorPointMoment(Guid.NewGuid(), Length.FromMeters(.6), Moment.FromNewtonMeters(50), "M1")],
            distributedLoads: [new(Guid.NewGuid(), Length.FromMeters(.2), Length.FromMeters(.8), ForcePerLength.FromNewtonsPerMeter(-500), "q1")]);
        var main = new MainWindowViewModel(b => { analyses++; return SpanDraft.Analysis.BeamAnalysis.Analyze(b); }); main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!;
        editor.Session.Commit(new(document, new()));
        int initialAnalyses = analyses;
        var view = new EditorView { DataContext = editor }; var window = new Window { Content = view };
        Arrange(window, 1250, 800);
        var surface = view.GetVisualDescendants().OfType<BeamEditorSurface>().Single();
        var canvas = view.GetVisualDescendants().OfType<BeamCanvas>().Single();
        var axis = view.GetVisualDescendants().OfType<CoordinateAxisPane>().Single();
        var layout = surface.StationLayout!; var frame = canvas.Scene!.Frame;
        var positions = canvas.Scene.Loads.Select(l => l.X).ToArray();
        var result = editor.Presentation.Result;
        var revision = editor.Session.CurrentRevision;
        foreach (var profile in new[] { UnitProfile.StructuralEngineering, UnitProfile.UnitedStates, PresentationTestProfiles.Mixed, UnitProfile.Default })
        {
            Assert.True(main.SetResultPresentation(profile, 3));
            Arrange(window, 1250, 800);
            Assert.Same(layout, surface.StationLayout);
            Assert.Equal(frame.Viewport, canvas.Scene!.Frame.Viewport);
            Assert.Equal(positions, canvas.Scene.Loads.Select(l => l.X));
            Assert.Equal(profile[QuantityKind.BeamLength].Symbol, axis.AxisLayout!.UnitSymbol);
            Assert.Equal(profile[QuantityKind.BeamLength].Symbol, editor.DimensionLength.Unit);
            Assert.Same(revision, editor.Session.CurrentRevision);
            Assert.Same(result, editor.Presentation.Result);
            Assert.Equal(initialAnalyses, analyses);
            Assert.Contains(canvas.Scene.Annotations, a => a.Text == PointLoadSymbol.Label(new(document.Loads[0].Position,
                PointLoadKind.Force, -1000), "F1", editor.ResultPresentation));
            Assert.All(view.GetVisualDescendants().OfType<ResultDiagram>(), d => Assert.Same(layout, d.Projection!.StationLayout));
        }
    }

    private static void Click(SettingsWindow window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Escape(SettingsWindow window) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

    /// <summary>
    /// The number formatting uses the same columns as the units: the digits field, the check box and the preview start at the
    /// field edge, nothing is clipped, and the preview columns line up across rows.
    /// </summary>
    private static void AssertNumericSection(Window window)
    {
        var content = (Control)((ScrollViewer)window.FindControl<ScrollViewer>("SettingsScroll")!).Content!;
        double X(Control control) => control.TranslatePoint(default, content)!.Value.X;
        var section = window.FindControl<Grid>("NumericSettings")!;
        var preview = window.FindControl<ItemsControl>("NumberPreviewRows")!;
        var scale = window.FindControl<DiscreteScaleSlider>("RoundingScale")!;
        var powers = window.FindControl<CheckBox>("PowersOfTenOption")!;
        Assert.Equal(X(scale), X(powers), 3);
        Assert.Equal(X(scale), X(preview), 3);
        Assert.Equal(X(scale), X(window.FindControl<TextBlock>("PresentationHint")!), 3);
        Assert.True(X(preview) + preview.Bounds.Width <= X(section) + section.Bounds.Width + .01);
        // The label of the preview stands in the label column like the labels of the fields.
        var previewLabel = section.Children.OfType<TextBlock>().Single(t => t.Text == Strings.NumberPreview);
        Assert.Equal(X(section.Children.OfType<TextBlock>().First()), X(previewLabel), 3);
        foreach (var text in section.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible))
        {
            Assert.True(text.TextLayout.Width <= text.Bounds.Width + .01, $"clipped: {text.Text}");
            Assert.True(text.TextLayout.Height <= text.Bounds.Height + .01, $"clipped: {text.Text}");
        }
        var rows = preview.GetVisualDescendants().OfType<Grid>().Where(g => g.DataContext is NumberPreviewRow).ToArray();
        Assert.Equal(3, rows.Length);
        for (int column = 0; column < 4; column++)
        {
            double x = X(rows[0].Children[column]);
            Assert.All(rows, row => Assert.Equal(x, X(row.Children[column]), 6));
        }
        var hint = window.FindControl<TextBlock>("PresentationHint")!;
        Assert.True(hint.TextLayout.Height <= hint.Bounds.Height + .01);
    }

    private static void Arrange(Window window, double width, double height)
    {
        window.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            foreach (var child in content.GetVisualDescendants().OfType<Control>())
            { child.InvalidateMeasure(); child.InvalidateArrange(); }
            content.InvalidateMeasure(); content.InvalidateArrange();
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Export(Control control, string name, double width, double height)
    {
        if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
        bitmap.Render(control); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

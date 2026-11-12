using static SpanDraft.Tests.DesktopControlEnvironment;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using SpanDraft.Reporting.Notation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Reporting;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SectionPreviewRenderingTests : SectionDefinitionPrototypeTestSupport
{
    private static bool LineIntersectsLabel(PreviewDrawingLine line, Rect rect)
    {
        // Clip the segment against the text box, without producing a bitmap.
        double low = 0, high = 1;
        Vector delta = line.End - line.Start;
        bool Clip(double direction, double distance)
        {
            if (Math.Abs(direction) < 1e-12) return distance >= 0;
            double fraction = distance / direction;
            if (direction < 0) low = Math.Max(low, fraction); else high = Math.Min(high, fraction);
            return low <= high;
        }
        return Clip(-delta.X, line.Start.X - rect.Left) && Clip(delta.X, rect.Right - line.Start.X)
            && Clip(-delta.Y, line.Start.Y - rect.Top) && Clip(delta.Y, rect.Bottom - line.Start.Y) && high > low;
    }

    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", false)]
    [InlineData("de-DE", true)] [InlineData("en-US", true)]
    public void ReportCompactAnnotationsFitTheSquareViewportWithoutCollisions(string culture, bool thinFlange)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        double side = ReportDocument.RightWidth / ReportVisuals.PointsPerDip;
        var size = new Size(side, side); var viewport = new Rect(size);
        foreach (var type in new[] { SectionEditorType.Rectangle, SectionEditorType.RectangularHollow,
            SectionEditorType.Circle, SectionEditorType.CircularHollow, SectionEditorType.ISection,
            SectionEditorType.USection, SectionEditorType.TSection, SectionEditorType.Angle })
        {
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm);
            if (thinFlange && type is SectionEditorType.ISection or SectionEditorType.USection or SectionEditorType.TSection)
            {
                Commit(vm, "r", "0"); Commit(vm, "tf", "1.7");
            }
            foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
            {
                var unit = units[QuantityKind.SectionDimension];
                Size Measure(TechnicalText text) => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12);
                var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, size, unit, Measure, compact: true);
                string context = $"{culture}/{type}/{unit.Symbol}/thin={thinFlange}";
                Assert.True(vm.Preview!.Dimensions.Count == layout.Dimensions.Count,
                    context + "/dimensions: " + string.Join(",", layout.Dimensions.Select(d => d.Key))
                    + "/bounds: " + layout.ProfileBounds + "/radius: " + (vm.Preview.Dimensions.TryGetValue("r", out var radius) ? radius.ToString() : "none"));
                foreach (var dimension in layout.Dimensions)
                {
                    Assert.True(viewport.Contains(dimension.TextBounds), context + "/" + dimension.Key + "/text");
                    Assert.Equal(dimension.Key is "h" or "tf", dimension.Rotated);
                    var measured = Measure(dimension.Content);
                    Assert.Equal(dimension.Rotated ? measured.Height : measured.Width, dimension.TextBounds.Width, 8);
                    Assert.Equal(dimension.Rotated ? measured.Width : measured.Height, dimension.TextBounds.Height, 8);
                    Assert.All(dimension.Arrows, arrow => Assert.True(viewport.Contains(arrow.Bounds), context + "/" + dimension.Key + "/arrow"));
                    Assert.All(dimension.Arrows, arrow => Assert.False(arrow.Bounds.Intersects(dimension.TextBounds), context + "/" + dimension.Key + "/own arrow"));
                    Assert.All(dimension.Lines, line =>
                    {
                        Assert.True(viewport.Contains(line.Start) && viewport.Contains(line.End), context + "/" + dimension.Key + "/line");
                        Assert.False(LineIntersectsLabel(line, dimension.TextBounds), context + "/" + dimension.Key + "/own line");
                    });
                    foreach (var other in layout.Dimensions.Where(d => d != dimension))
                    {
                        Assert.False(dimension.TextBounds.Inflate(2).Intersects(other.TextBounds), context + "/" + dimension.Key + "/" + other.Key);
                        Assert.All(other.Lines, line => Assert.False(LineIntersectsLabel(line, dimension.TextBounds), context + "/" + dimension.Key + "/" + other.Key + "/line"));
                        Assert.All(other.Arrows, arrow => Assert.False(arrow.Bounds.Intersects(dimension.TextBounds), context + "/" + dimension.Key + "/" + other.Key + "/arrow"));
                    }
                    if (dimension.Key is "h" or "tf" or "b" or "d" or "D" or "t")
                        Assert.False(layout.ProfileBounds.Intersects(dimension.TextBounds), context + "/" + dimension.Key + "/profile");
                    if (dimension.Key == "b")
                    {
                        double measureY = dimension.Lines[2].Start.Y;
                        Assert.Equal(layout.ProfileBounds.Bottom + 28, measureY, 8);
                        Assert.InRange(dimension.TextBounds.Top - measureY, 0, 4 + 1e-8);
                    }
                    if (dimension.Key == "tf")
                    {
                        Assert.True(dimension.TextBounds.Right < layout.ProfileBounds.Left, context + "/tf text left of profile");
                        Assert.All(dimension.Arrows, arrow => Assert.True(arrow.Bounds.Right < layout.ProfileBounds.Left, context + "/tf arrow left of profile"));
                        Assert.All(dimension.Lines.Take(2), line =>
                        {
                            Assert.True(line.Start.X < layout.ProfileBounds.Left && line.End.X < line.Start.X,
                                context + "/tf left extension");
                            Assert.Equal(line.Start.Y, line.End.Y, 8);
                        });
                        var physical = vm.Preview.Dimensions["tf"];
                        Assert.Equal(layout.Project(physical.Start).Y, dimension.Arrows[0].Tip.Y, 8);
                        Assert.Equal(layout.Project(physical.End).Y, dimension.Arrows[1].Tip.Y, 8);
                        var measureLine = dimension.Lines[2];
                        var height = layout.Dimensions.Single(d => d.Key == "h");
                        Assert.Equal(layout.ProfileBounds.Left - 22, measureLine.Start.X, 8);
                        Assert.True(height.Lines[2].Start.X < measureLine.Start.X, context + "/h before tf");
                        Assert.True(height.TextBounds.Right < dimension.TextBounds.Left, context + "/h text before tf");
                        Assert.True(height.TextBounds.Right < height.Lines[2].Start.X, context + "/h caption left of line");
                        Assert.True(dimension.TextBounds.Right < measureLine.Start.X, context + "/tf caption left of line");
                        Assert.True(Math.Min(measureLine.Start.Y, measureLine.End.Y) <= dimension.TextBounds.Top);
                        Assert.True(Math.Max(measureLine.Start.Y, measureLine.End.Y) >= dimension.TextBounds.Bottom);
                        double flangeCenter = (dimension.Arrows[0].Tip.Y + dimension.Arrows[1].Tip.Y) / 2;
                        if (type != SectionEditorType.TSection)
                            Assert.True(dimension.TextBounds.Center.Y < flangeCenter, context + "/tf caption on upper extension");
                        Assert.All(dimension.Arrows, arrow => Assert.False(arrow.Bounds.Intersects(layout.ProfileBounds), context + "/tf arrow profile"));
                    }
                }
                foreach (var axis in layout.Axes)
                {
                    Assert.True(viewport.Contains(axis.LabelBounds), context + "/axis label");
                    Assert.True(viewport.Contains(axis.ArrowBounds), context + "/axis arrow");
                    Assert.False(layout.ProfileBounds.Intersects(axis.LabelBounds), context + "/axis profile");
                    var baseCenter = (axis.ArrowPoints[1] + (Vector)axis.ArrowPoints[2]) / 2;
                    Assert.Equal(baseCenter.X, axis.SolidEnd(true).X, 10);
                    Assert.Equal(baseCenter.Y, axis.SolidEnd(true).Y, 10);
                    Assert.Equal(axis.End, axis.SolidEnd(false));
                    foreach (var other in layout.Axes)
                    {
                        Assert.False(other.ArrowBounds.Intersects(axis.LabelBounds), context + "/axis arrow label");
                        Assert.False(LineIntersectsLabel(new(other.Start, other.End), axis.LabelBounds), context + "/axis line label");
                    }
                    foreach (var dimension in layout.Dimensions)
                    {
                        Assert.False(axis.LabelBounds.Intersects(dimension.TextBounds), context + "/axis dimension");
                        Assert.False(axis.ArrowBounds.Intersects(dimension.TextBounds), context + "/arrow dimension");
                        Assert.All(dimension.Lines, line => Assert.False(LineIntersectsLabel(line, axis.LabelBounds), context + "/axis dimension line"));
                    }
                }
                // The editor keeps horizontal captions; its existing geometry tests
                // separately protect the normal margins, dimensions and axis overhangs.
                var editor = SectionPreviewDrawingLayout.Create(vm.Preview!, new(520, 440), unit, Measure);
                Assert.All(editor.Dimensions, d => Assert.False(d.Rotated));
            }
        }
    }

    [Theory]
    [InlineData("80")] [InlineData("120")] [InlineData("200")]
    public void CompactYTriesClearPositionsAlongTheAxisBeforeMovingVertically(string height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection); Fill(vm);
        Commit(vm, "h", height);
        double side = ReportDocument.RightWidth / ReportVisuals.PointsPerDip;
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        foreach (double textWidthScale in new[] { 1d, 1.25d })
        {
            Size Measure(TechnicalText text)
            {
                var measured = SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12);
                return new(text.PlainText.Contains(" = ", StringComparison.Ordinal) ? measured.Width * textWidthScale : measured.Width, measured.Height);
            }
            var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, new(side, side),
                units[QuantityKind.SectionDimension], Measure, compact: true);
            var y = layout.Axes.Single(a => a.Axis == SectionAxisDesignation.Y);
            Assert.True(y.LabelBounds.Left > y.End.X);
            Vector direction = y.End - y.Start; direction /= direction.Length;
            var normal = new Vector(-direction.Y, direction.X);
            Rect Candidate(double distance, double offset)
            {
                Point center = y.End + direction * distance + normal * offset;
                return new(Math.Clamp(center.X - y.LabelBounds.Width / 2, 4, side - y.LabelBounds.Width - 4),
                    Math.Clamp(center.Y - y.LabelBounds.Height / 2, 4, side - y.LabelBounds.Height - 4),
                    y.LabelBounds.Width, y.LabelBounds.Height);
            }
            bool IsClear(Rect label) => new Rect(new Size(side, side)).Contains(label)
                && !layout.ProfileBounds.Intersects(label)
                && layout.Axes.All(axis => !axis.ArrowBounds.Inflate(2).Intersects(label)
                    && !LineIntersectsLabel(new(axis.Start, axis.End), label.Inflate(2)))
                && layout.Axes.Where(axis => axis != y).All(axis => !axis.LabelBounds.Inflate(4).Intersects(label))
                && layout.Dimensions.All(d => !d.TextBounds.Inflate(2).Intersects(label)
                    && d.Lines.All(line => !LineIntersectsLabel(line, label))
                    && d.Arrows.All(arrow => !arrow.Bounds.Intersects(label)));
            var candidates = new[] { 0d, 20d, -20d, 40d, -40d, 60d, -60d }
                .SelectMany(offset => new[] { 10d, 26d, 42d }.Select(distance => Candidate(distance, offset))).ToArray();
            var clear = candidates.Where(IsClear).ToArray();
            Assert.NotEmpty(clear);
            Assert.Equal(clear[0], y.LabelBounds);
            Assert.True(IsClear(y.LabelBounds));

            var editor = SectionPreviewDrawingLayout.Create(vm.Preview!, new(520, 440),
                units[QuantityKind.SectionDimension], Measure);
            var width = editor.Dimensions.Single(d => d.Key == "b");
            Assert.Equal(16, width.TextBounds.Top - width.Lines[2].Start.Y, 8);
            var flange = editor.Dimensions.Single(d => d.Key == "tf");
            Assert.False(flange.Rotated);
            Assert.All(flange.Arrows, arrow => Assert.True(arrow.Bounds.Left > editor.ProfileBounds.Right));
        }
    }
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1250, 800)]
    public void ProductControlsKeepParametricColumnsAndAxisBlocksSelectByMouseAndKeyboard(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view }; window.Show(); ArrangeSection(window, width, height);
        var columns = view.FindControl<Grid>("DefinitionColumns")!;
        Assert.Single(columns.ColumnDefinitions);
        Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Button>("CancelButton")!.IsEffectivelyVisible);
        Assert.Null(view.FindControl<StackPanel>("AxisOptions"));
        vm.SelectForm(SectionEditorType.ISection); ArrangeSection(window, width, height);
        Assert.Equal(3, columns.ColumnDefinitions.Count);
        Assert.True(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        Assert.Equal(SectionEditorType.ISection, view.FindControl<SectionShapeIcon>("SelectedFormIcon")!.Type);
        Assert.Null(view.FindControl<TextBlock>("SelectedFormLabel"));
        Assert.Equal(vm.DisplayName, view.FindControl<TextBlock>("DefinitionTitle")!.Text);
        var canvas = view.FindControl<SectionPreviewControl>("SectionPreview")!;
        Assert.InRange(canvas.Bounds.Width, 180, width); Assert.InRange(canvas.Bounds.Height, 180, height);
        var referenceLayout = SectionPreviewDrawingLayout.Create(canvas.Geometry!, canvas.Bounds.Size,
            canvas.UnitProfile[QuantityKind.SectionDimension], text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
        Assert.Equal(new[] { SectionAxisDesignation.Y, SectionAxisDesignation.Z }, referenceLayout.Axes.Select(a => a.Axis));
        Assert.All(referenceLayout.Axes, a => Assert.True(a.IsReference)); Assert.Null(canvas.Geometry!.Centroid);
        Assert.Null(canvas.SelectedAxis);
        Fill(vm, false); ArrangeSection(window, width, height);
        var realLayout = SectionPreviewDrawingLayout.Create(canvas.Geometry!, canvas.Bounds.Size,
            canvas.UnitProfile[QuantityKind.SectionDimension], text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
        Assert.All(realLayout.Axes, a => Assert.False(a.IsReference)); Assert.NotNull(canvas.Geometry!.Centroid);
        Assert.Equal(3, columns.ColumnDefinitions.Count);
        Assert.True(view.FindControl<TextBlock>("AxisSelectionHint")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled); Assert.Null(canvas.SelectedAxis);
        var blocks = view.FindControl<ItemsControl>("AxisValueList")!.GetVisualDescendants().OfType<SectionAxisBlock>().ToArray();
        Assert.Equal(2, blocks.Length);
        var pointer = new Pointer(71, PointerType.Mouse, true);
        var position = blocks[1].TranslatePoint(new Point(blocks[1].Bounds.Width - 3, blocks[1].Bounds.Height - 3), window)!.Value;
        blocks[1].RaiseEvent(new PointerPressedEventArgs(blocks[1], pointer, window, position, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        blocks[1].RaiseEvent(new PointerReleasedEventArgs(blocks[1], pointer, window, position, 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis); Assert.Equal(vm.SelectedAxis, canvas.SelectedAxis);
        blocks[0].Focus(); blocks[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        Assert.Equal(SectionAxisDesignation.Y, vm.SelectedAxis); Assert.True(view.FindControl<Button>("ConfirmButton")!.IsEffectivelyEnabled);
        Assert.True(blocks[0].IsSelected); Assert.False(blocks[1].IsSelected);
        vm.ChangeFormCommand.Execute(null); ArrangeSection(window, width, height);
        Assert.Single(columns.ColumnDefinitions); Assert.False(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        vm.SelectForm(SectionEditorType.Manual); ArrangeSection(window, width, height);
        Assert.Equal(2, columns.ColumnDefinitions.Count); Assert.False(view.FindControl<Grid>("PreviewColumn")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Grid>("ValuesColumn")!.IsEffectivelyVisible);
        window.Close();
    }
    [Theory]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void LargeRadiiDoNotMoveThicknessDimensionsOntoFillets(SectionEditorType type)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(new(UnitProfile.Default.WithUnit(QuantityKind.SectionDimension, UnitCatalog.Meter))); vm.SelectForm(type);
        foreach (var input in vm.Parameters.ToArray()) Commit(vm, input.Key, input.Key switch
        { "b" => "0.125", "h" => "0.25", "tw" or "tf" or "t" => "0.015625", "r" => type == SectionEditorType.USection ? "0.09375" : type == SectionEditorType.Angle ? "0.109375" : "0.0546875", _ => "" });
        Assert.NotNull(vm.Section);
        var geometry = vm.Preview!; string key = type == SectionEditorType.Angle ? "t" : "tf";
        var dimension = geometry.Dimensions[key];
        Assert.Equal(geometry.Width, dimension.Start.X);
        Assert.Equal(geometry.Width, dimension.End.X);
        foreach (var point in new[] { dimension.Start, dimension.End })
            Assert.Contains(geometry.Contours.SelectMany(c => c), segment => segment.Center is null
                && Math.Abs(segment.Start.X - point.X) < 1e-12 && Math.Abs(segment.End.X - point.X) < 1e-12
                && point.Y >= Math.Min(segment.Start.Y, segment.End.Y) - 1e-12 && point.Y <= Math.Max(segment.Start.Y, segment.End.Y) + 1e-12);
        foreach (var size in new[] { new Size(250, 360), new Size(420, 440), new Size(520, 440) })
        {
            var layout = SectionPreviewDrawingLayout.Create(geometry, size, UnitCatalog.Meter,
                text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
            Assert.Equal(geometry.Dimensions.Count, layout.Dimensions.Count);
            Assert.Single(layout.Dimensions.Single(d => d.Key == "r").Lines);
            foreach (var drawing in layout.Dimensions.Where(d => d.Key is "t" or "tw" or "tf"))
            {
                if (drawing.Key == "tw") continue;
                Assert.False(LineIntersectsLabel(drawing.Lines[2], layout.ProfileBounds));
                Assert.All(drawing.Arrows, arrow => Assert.False(arrow.Bounds.Intersects(layout.ProfileBounds)));
            }
            foreach (var drawing in layout.Dimensions)
            {
                if (drawing.Key is not ("r" or "tw"))
                    Assert.False(drawing.TextBounds.Intersects(layout.ProfileBounds));
                foreach (var other in layout.Dimensions.Where(d => d.Key != drawing.Key))
                {
                    Assert.False(drawing.TextBounds.Inflate(2).Intersects(other.TextBounds));
                    Assert.All(other.Lines, line => Assert.False(LineIntersectsLabel(line, drawing.TextBounds)));
                }
            }
        }
    }
    [Theory]
    [InlineData("0.1")] [InlineData("0.0000001")]
    public void SubpixelWallsKeepTheirActualMeasureAndHaveTwoExtensionLines(string thickness)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection); Fill(vm, false);
        Commit(vm, "tw", thickness); Assert.NotNull(vm.Section);
        var geometry = vm.Preview!;
        var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter, text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
        var dimension = layout.Dimensions.Single(d => d.Key == "tw");
        Assert.Equal(2, dimension.Arrows.Count); Assert.True(dimension.Lines.Count >= 3);
        Assert.Equal(geometry.Dimensions["tw"].Value * layout.Scale, ((Vector)(dimension.Arrows[1].Tip - dimension.Arrows[0].Tip)).Length, 10);
        Assert.True(dimension.Arrows[0].TailDirection.X < 0); Assert.True(dimension.Arrows[1].TailDirection.X > 0);
    }
    [Theory]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void ConcaveRadiusLeaderPointsIntoTheOpeningAndNeverBacktracksThroughTheFlange(SectionEditorType type)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
        foreach (bool complete in new[] { false, true })
        {
            if (complete) Fill(vm, false);
            var geometry = vm.Preview!;
            var layout = SectionPreviewDrawingLayout.Create(geometry, new(520, 440), UnitCatalog.Millimeter,
                text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
            var radius = geometry.Dimensions["r"];
            var drawing = layout.Dimensions.Single(d => d.Key == "r");
            var arrow = Assert.Single(drawing.Arrows);
            Vector towardOpening = layout.Project(radius.Start) - layout.Project(radius.End);
            Assert.True(arrow.TailDirection.X * towardOpening.X + arrow.TailDirection.Y * towardOpening.Y > 0,
                $"{type}: radius leader enters material instead of the opening");
            Assert.All(drawing.Lines, line => Assert.True(line.End.X >= line.Start.X - .001, $"{type}: leader doubles back"));
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void AllAnnotationLabelsAndLinesStaySeparatedAtEveryCommitAndViewportSize(string culture)
    {
        using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        {
            var vm = new SectionDefinitionViewModel(new(units));
            foreach (var form in vm.Forms)
            {
                vm.SelectForm(form.Value); Check();
                foreach (var input in vm.Parameters.ToArray())
                {
                    Commit(vm, input.Key, input.Key switch { "b" => "120", "h" => "200", "d" or "D" => "80", "t" or "tw" => "5", "tf" => "12", "r" => "8", _ => "0" });
                    Check();
                }
                if (vm.Parameters.Any(p => p.Key == "r")) { Commit(vm, "r", "0"); Check(); }
                if (vm.Parameters.Any(p => p.Key == "h"))
                {
                    Commit(vm, "h", "2000"); Check();
                    Commit(vm, "b", "2000"); Commit(vm, "h", "120"); Check();
                }
            }
            void Check()
            {
                var geometry = vm.Preview!;
                foreach (var size in new[] { new Size(250, 360), new Size(250, 400), new Size(300, 320), new Size(380, 360), new Size(520, 440) })
                {
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                        text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
                    foreach (var dimension in layout.Dimensions.Where(d => d.Key is "t" or "tw" or "tf"))
                    {
                        Assert.All(dimension.Lines, line =>
                        {
                            Assert.True(new Rect(size).Contains(line.Start), $"{formName()}/{size}: clipped {dimension.Key} line start");
                            Assert.True(new Rect(size).Contains(line.End), $"{formName()}/{size}: clipped {dimension.Key} line end");
                        });
                        if (dimension.Key == "tw")
                        {
                            Assert.Equal(layout.ProfileBounds.Bottom - layout.ProfileBounds.Height * (geometry.Type == SectionEditorType.TSection ? .25 : .70),
                                dimension.Arrows[0].Tip.Y, 10);
                            continue;
                        }
                        Assert.False(LineIntersectsLabel(dimension.Lines[2], layout.ProfileBounds),
                            $"{formName()}/{size}: {dimension.Key} measure covers material");
                        Assert.All(dimension.Arrows, arrow =>
                        {
                            Assert.True(new Rect(size).Contains(arrow.Bounds), $"{formName()}/{size}: clipped {dimension.Key} arrow");
                            Assert.False(arrow.Bounds.Intersects(layout.ProfileBounds), $"{formName()}/{size}: {dimension.Key} filled arrow covers material");
                        });
                    }
                    var labels = layout.Dimensions.Select(d => (d.Key, d.TextBounds))
                        .Concat(layout.Axes.Select(a => (Key: a.Axis.ToString(), TextBounds: a.LabelBounds))).ToArray();
                    Assert.Equal(geometry.Dimensions.Keys.Order(), layout.Dimensions.Select(d => d.Key).Order());
                    for (int i = 0; i < labels.Length; i++)
                    {
                        var label = labels[i];
                        Assert.True(label.TextBounds.Left >= 0 && label.TextBounds.Right <= size.Width && label.TextBounds.Top >= 0 && label.TextBounds.Bottom <= size.Height,
                            $"{formName()}/{size}: {label.Key} outside viewport");
                        // Geometry-attached detail captions may use a concave profile's empty spaces.
                        if (label.Key is not ("r" or "tw"))
                            Assert.False(layout.ProfileBounds.Intersects(label.TextBounds), $"{formName()}/{size}: {label.Key} text covers profile");
                        for (int j = i + 1; j < labels.Length; j++) Assert.False(label.TextBounds.Inflate(2).Intersects(labels[j].TextBounds),
                            $"{formName()}/{size}/{units[QuantityKind.SectionDimension].Symbol}: {label.Key}/{labels[j].Key} texts overlap; profile={layout.ProfileBounds}; labels={string.Join("; ", labels.Select(l => l.Key + ":" + l.TextBounds))}");
                        foreach (var dimension in layout.Dimensions.Where(d => d.Key != label.Key))
                        {
                            foreach (var line in dimension.Lines)
                                Assert.False(LineIntersectsLabel(line, label.TextBounds), $"{formName()}/{size}/{units[QuantityKind.SectionDimension].Symbol}: {dimension.Key} line {line.Start}→{line.End} crosses {label.Key} text {label.TextBounds}; profile={layout.ProfileBounds}; labels={string.Join("; ", labels.Select(l => l.Key + ":" + l.TextBounds))}");
                            foreach (var arrow in dimension.Arrows)
                                Assert.False(arrow.Bounds.Intersects(label.TextBounds), $"{formName()}/{size}: {dimension.Key} arrow crosses {label.Key} text");
                        }
                    }
                }
                string formName() => vm.Type!.Value.ToString();
            }
        }
    }
    [Fact]
    public void FixedTWebMeasureKeepsConfiguredHeightWithoutMovingTheTrueCentroidAxis()
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.TSection); Fill(vm, false);
        Commit(vm, "r", "0"); Commit(vm, "tf", "1.7"); Assert.NotNull(vm.Section);
        var geometry = vm.Preview!;
        var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter,
            text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
        var axis = layout.Axes.Single(a => a.Axis == SectionAxisDesignation.Y);
        var initialMeasureY = layout.Project(geometry.Dimensions["tw"].Start).Y - 24;
        Assert.InRange(Math.Abs(initialMeasureY - axis.Start.Y), 0, 1);
        var web = layout.Dimensions.Single(d => d.Key == "tw");
        Assert.Equal(layout.ProfileBounds.Bottom - layout.ProfileBounds.Height * .25, web.Arrows[0].Tip.Y, 10);
        Assert.Equal(layout.Project(geometry.Centroid!.Value).Y, axis.Start.Y, 10);
        Assert.True(Math.Abs(web.Arrows[0].Tip.Y - axis.Start.Y) >= 6);
        Assert.Equal(3, web.Lines.Count);
    }
    [Theory]
    [InlineData("de-DE", SectionEditorType.ISection)] [InlineData("en-US", SectionEditorType.ISection)]
    [InlineData("de-DE", SectionEditorType.USection)] [InlineData("en-US", SectionEditorType.USection)]
    [InlineData("de-DE", SectionEditorType.TSection)] [InlineData("en-US", SectionEditorType.TSection)]
    public void WebMeasureStaysAtConfiguredHeightAndCaptionFollowsWebAcrossEditsUnitsAndViewportChanges(string culture, SectionEditorType type)
    {
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Check();
        Fill(vm, false); Check();
        Commit(vm, "tw", "0.0000001"); Check();
        Commit(vm, "h", "2000"); Check();
        Commit(vm, "b", "2000"); Commit(vm, "h", "120"); Check();

        void Check()
        {
            var geometry = vm.Preview!;
            foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
            foreach (var size in new[] { new Size(250, 360), new Size(380, 360), new Size(520, 440) })
            {
                var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                    text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
                var web = layout.Dimensions.Single(d => d.Key == "tw");
                double y = layout.ProfileBounds.Bottom - layout.ProfileBounds.Height * (type == SectionEditorType.TSection ? .25 : .70);
                Assert.All(web.Arrows, arrow => Assert.Equal(y, arrow.Tip.Y, 10));
                Assert.Equal(y, web.Lines[2].Start.Y, 10); Assert.Equal(y, web.Lines[2].End.Y, 10);
                Assert.Equal(y, web.TextBounds.Center.Y, 10);
                Assert.True(web.TextBounds.Left >= web.Arrows[1].Tip.X + 28 - 1e-10);
                Assert.Equal(web.TextBounds.Left - 4, web.Lines[2].End.X, 10);
                Assert.Equal(geometry.Dimensions["tw"].Value * layout.Scale,
                    ((Vector)(web.Arrows[1].Tip - web.Arrows[0].Tip)).Length, 10);
            }
        }
    }
    [Theory]
    [InlineData("de-DE", SectionEditorType.ISection)] [InlineData("en-US", SectionEditorType.ISection)]
    [InlineData("de-DE", SectionEditorType.USection)] [InlineData("en-US", SectionEditorType.USection)]
    [InlineData("de-DE", SectionEditorType.TSection)] [InlineData("en-US", SectionEditorType.TSection)]
    public void WebCaptionUsesScreenSpaceGapFromWebRatherThanFlangeOrViewportEdge(string culture, SectionEditorType type)
    {
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Fill(vm, false);
        Commit(vm, "h", "400"); Commit(vm, "r", "0");
        foreach (string width in new[] { "80", "120", "240", "120" })
        {
            Commit(vm, "b", width);
            var geometry = vm.Preview!;
            foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
            foreach (var size in new[] { new Size(380, 360), new Size(520, 440) })
            {
                var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                    text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
                var web = layout.Dimensions.Single(d => d.Key == "tw");
                double preferredLeft = layout.Project(geometry.Dimensions["tw"].End).X + 28;
                if (type != SectionEditorType.TSection || Math.Abs(preferredLeft - web.TextBounds.Left) < 1e-10)
                    Assert.Equal(preferredLeft, web.TextBounds.Left, 10);
                else
                {
                    Assert.True(web.TextBounds.Left > preferredLeft);
                    var preferredCaption = new Rect(new Point(preferredLeft, web.TextBounds.Top), web.TextBounds.Size);
                    Assert.Contains(layout.Dimensions.Single(d => d.Key == "b").Lines.Take(2),
                        line => LineIntersectsLabel(line, preferredCaption.Inflate(2)));
                }
                Assert.Equal(web.TextBounds.Left - 4, web.Lines[2].End.X, 10);
                Assert.True(web.TextBounds.Top > layout.ProfileBounds.Top + geometry.Dimensions["tf"].Value * layout.Scale);
                Assert.True(web.TextBounds.Bottom < layout.ProfileBounds.Bottom - geometry.Dimensions["tf"].Value * layout.Scale);
                Assert.All(layout.Dimensions.Where(d => d.Key != "tw"), other =>
                    Assert.False(other.TextBounds.Inflate(2).Intersects(web.TextBounds)));
            }
        }
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void DirectDimensionsRetainExactEdgesAndUseUniformSlenderTriangleArrows(SectionEditorType type)
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
        foreach (bool complete in new[] { false, true })
        {
            if (complete) Fill(vm, false);
            var geometry = vm.Preview!;
            var layout = SectionPreviewDrawingLayout.Create(geometry, new(420, 440), UnitCatalog.Millimeter,
                text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
            Assert.Equal(geometry.Dimensions.Keys.Order(), layout.Dimensions.Select(d => d.Key).Order());
            foreach (var dimension in layout.Dimensions)
            {
                foreach (var arrow in dimension.Arrows)
                {
                    Assert.Equal(3, arrow.Points.Count);
                    Assert.Equal(4, ((Vector)(arrow.Points[2] - arrow.Points[1])).Length, 10);
                    var baseCenter = arrow.Points[1] + (arrow.Points[2] - arrow.Points[1]) / 2;
                    Assert.Equal(13.2, ((Vector)(baseCenter - arrow.Tip)).Length, 10);
                }
                if (dimension.Key == "r")
                {
                    var line = Assert.Single(dimension.Lines); var arrow = Assert.Single(dimension.Arrows);
                    Assert.Equal(layout.Project(geometry.Dimensions["r"].End), line.Start);
                    Assert.Equal(line.Start, arrow.Tip);
                    Assert.Equal(complete ? "r = 8 mm" : "r", dimension.Text);
                    Assert.True(line.Start.X != line.End.X && line.Start.Y != line.End.Y);
                }
                else
                {
                    Assert.Equal(2, dimension.Arrows.Count);
                    Assert.Equal(geometry.Dimensions[dimension.Key].Value * layout.Scale,
                        ((Vector)(dimension.Arrows[1].Tip - dimension.Arrows[0].Tip)).Length, 10);
                    Assert.Equal(3, dimension.Lines.Count);
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void RadiusCaptionsUseDimensionUnitsAndPreferredLeaderIsAttachedToGeometry(string culture)
    {
        using var scope = new UiCultureScope(culture);
        foreach (var type in new[] { SectionEditorType.RectangularHollow, SectionEditorType.ISection,
            SectionEditorType.USection, SectionEditorType.TSection, SectionEditorType.Angle })
        {
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
            foreach (int state in new[] { 0, 1, 2 })
            {
                if (state == 1) Fill(vm, false);
                if (state == 2) Commit(vm, "r", "0");
                var geometry = vm.Preview!; var radius = geometry.Dimensions["r"];
                foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
                foreach (var size in new[] { new Size(380, 360), new Size(520, 440) })
                {
                    var unit = units[QuantityKind.SectionDimension];
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, unit,
                        text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
                    var drawing = layout.Dimensions.Single(d => d.Key == "r");
                    var line = Assert.Single(drawing.Lines); var arrow = Assert.Single(drawing.Arrows);
                    Assert.Equal(radius.IsConfirmed ? QuantityFormatter.FormatInputEquation("r", radius.Value, unit) : "r", drawing.Text);
                    Assert.Equal(layout.Project(radius.End), line.Start); Assert.Equal(line.Start, arrow.Tip);
                    Vector ray = line.End - line.Start;
                    double angle = Math.Atan2(Math.Abs(ray.Y), ray.X) * 180 / Math.PI;
                    if (type != SectionEditorType.TSection || Math.Abs(angle - 35) < 1e-10 && Math.Abs(ray.Length - 32) < 1e-10)
                    {
                        Assert.Equal(32, ray.Length, 10);
                        Assert.Equal(35, angle, 10);
                    }
                    else
                    {
                        // The new long flange extensions can obstruct the preferred
                        // leader or caption. Keep the geometry attachment and avoid them.
                        Assert.InRange(ray.Length, 32 - 1e-10, 320 + 1e-10);
                        double radians = 35 * Math.PI / 180;
                        Point preferredEnd = line.Start + new Vector(Math.Cos(radians), Math.Sign(ray.Y) * Math.Sin(radians)) * 32;
                        var preferredCaption = new Rect(new Point(preferredEnd.X + 4, preferredEnd.Y - drawing.TextBounds.Height / 2), drawing.TextBounds.Size);
                        var preferredLeader = new PreviewDrawingLine(line.Start, preferredEnd);
                        Assert.Contains(layout.Dimensions.Single(d => d.Key == "b").Lines.Take(2), extension =>
                            LineIntersectsLabel(extension, preferredCaption.Inflate(2)) || LineIntersectsLabel(preferredLeader,
                                new Rect(new Point(extension.Start.X - .5, Math.Min(extension.Start.Y, extension.End.Y)),
                                    new Size(1, Math.Abs(extension.End.Y - extension.Start.Y)))));
                    }
                    Assert.Equal(line.End.X + 4, drawing.TextBounds.Left, 10);
                    Assert.Equal(line.End.Y, drawing.TextBounds.Center.Y, 10);
                    Assert.Equal(ray.X / ray.Length, arrow.TailDirection.X, 10);
                    Assert.Equal(ray.Y / ray.Length, arrow.TailDirection.Y, 10);
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void WidthDimensionsStayBelowProfilesAndTExtensionsReachActualFlangeCorners(string culture)
    {
        using var scope = new UiCultureScope(culture);
        foreach (var type in new[] { SectionEditorType.Rectangle, SectionEditorType.RectangularHollow,
            SectionEditorType.ISection, SectionEditorType.USection, SectionEditorType.TSection, SectionEditorType.Angle })
        {
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type);
            foreach (bool complete in new[] { false, true })
            {
                if (complete) Fill(vm, false);
                var geometry = vm.Preview!;
                foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
                foreach (var size in new[] { new Size(250, 360), new Size(380, 360), new Size(520, 440) })
                {
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                        text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
                    var width = layout.Dimensions.Single(d => d.Key == "b");
                    Assert.Equal(3, width.Lines.Count); Assert.Equal(2, width.Arrows.Count);
                    Assert.Equal(layout.ProfileBounds.Left, width.Arrows[0].Tip.X, 10);
                    Assert.Equal(layout.ProfileBounds.Right, width.Arrows[1].Tip.X, 10);
                    Assert.All(width.Arrows, arrow => Assert.Equal(layout.ProfileBounds.Bottom + 28, arrow.Tip.Y, 10));
                    Assert.True(width.TextBounds.Top > layout.ProfileBounds.Bottom);
                    var height = layout.Dimensions.Single(d => d.Key == "h");
                    Assert.Equal(layout.ProfileBounds.Bottom, height.Arrows[0].Tip.Y, 10);
                    Assert.Equal(layout.ProfileBounds.Top, height.Arrows[1].Tip.Y, 10);
                    Assert.Equal(layout.ProfileBounds.Left - 22, height.Arrows[0].Tip.X, 10);
                    Assert.Equal(layout.ProfileBounds.Left - 27, height.Lines[0].End.X, 10);
                    Assert.Equal(layout.ProfileBounds.Bottom, height.Lines[0].Start.Y, 10);
                    Assert.Equal(layout.ProfileBounds.Left - 5, height.Lines[1].Start.X, 10);
                    if (type == SectionEditorType.TSection)
                    {
                        // The lower height extension references the actual web, with the same
                        // five-DIP contour clearance used by the upper flange extension.
                        double webLeft = layout.Project(geometry.Dimensions["tw"].Start).X;
                        Assert.Equal(webLeft - 5, height.Lines[0].Start.X, 10);
                        double physicalWebLeft = (geometry.Width - geometry.Dimensions["tw"].Value) / 2;
                        Assert.Contains(geometry.Contours.SelectMany(c => c), segment =>
                            (Math.Abs(segment.Start.X - physicalWebLeft) < 1e-12 && Math.Abs(segment.Start.Y) < 1e-12)
                            || (Math.Abs(segment.End.X - physicalWebLeft) < 1e-12 && Math.Abs(segment.End.Y) < 1e-12));
                        double flangeBottom = geometry.Height - geometry.Dimensions["tf"].Value;
                        Assert.Equal(layout.Project(new Point(0, flangeBottom)) + new Vector(0, 5), width.Lines[0].Start);
                        Assert.Equal(layout.Project(new Point(geometry.Width, flangeBottom)) + new Vector(0, 5), width.Lines[1].Start);
                        foreach (var corner in new[] { new Point(0, flangeBottom), new Point(geometry.Width, flangeBottom) })
                            Assert.Contains(geometry.Contours.SelectMany(c => c), segment =>
                                ((Vector)(segment.Start - corner)).Length < 1e-12 || ((Vector)(segment.End - corner)).Length < 1e-12);
                        Assert.All(width.Lines.Take(2), line =>
                        {
                            Assert.Equal(flangeBottom * layout.Scale + 28, line.End.Y - line.Start.Y, 10);
                            Assert.Equal(layout.ProfileBounds.Bottom + 33, line.End.Y, 10);
                            Assert.True(line.End.Y > line.Start.Y);
                        });
                        Assert.Equal(complete ? QuantityFormatter.FormatInputEquation("b", geometry.Width, units[QuantityKind.SectionDimension]) : "b", width.Text);
                    }
                    else
                    {
                        Assert.Equal(layout.ProfileBounds.Left - 5, height.Lines[0].Start.X, 10);
                    }
                }
            }
        }
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void ReferenceYZAxesExistImmediatelyWithoutInventingSectionPropertiesAndYieldToCoreAxes(SectionEditorType type)
    {
        foreach (string culture in new[] { "de-DE", "en-US" })
        {
            using var scope = new UiCultureScope(culture);
            var vm = new SectionDefinitionViewModel(); vm.SelectForm(type); Check(true);
            var preview = vm.Preview;
            vm.Parameters[0].PendingText = "200";
            Assert.Same(preview, vm.Preview); Check(true);
            if (vm.Parameters.Count > 1) { vm.Parameters[0].Commit(); Check(true); }
            Fill(vm, false); Check(false);

            void Check(bool reference)
            {
                var geometry = vm.Preview!;
                if (reference)
                {
                    Assert.Null(vm.Section); Assert.Null(geometry.Centroid); Assert.Null(geometry.PrincipalAngle);
                    Assert.Equal("—", vm.Area); Assert.False(vm.CanConfirm); Assert.Null(vm.SelectedAxis);
                }
                foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
                foreach (var size in new[] { new Size(250, 360), new Size(300, 320), new Size(520, 440) })
                {
                    var layout = SectionPreviewDrawingLayout.Create(geometry, size, units[QuantityKind.SectionDimension],
                        text => SchematicText.Measure(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12));
                    Assert.Equal(2, layout.Axes.Count);
                    Assert.All(layout.Axes, axis => Assert.Equal(reference, axis.IsReference));
                    var center = reference ? layout.ProfileBounds.Center : layout.Project(geometry.Centroid!.Value);
                    foreach (var axis in layout.Axes)
                    {
                        Vector ray = axis.End - axis.Start, offset = center - axis.Start;
                        Assert.InRange(Math.Abs(ray.X * offset.Y - ray.Y * offset.X), 0, 1e-8);
                        Assert.False(layout.ProfileBounds.Contains(axis.Start)); Assert.False(layout.ProfileBounds.Contains(axis.End));
                        ray /= ray.Length;
                        double DistanceToEdge(Vector direction)
                        {
                            var bounds = layout.ProfileBounds;
                            double horizontal = Math.Abs(direction.X) < 1e-12 ? double.PositiveInfinity
                                : ((direction.X > 0 ? bounds.Right : bounds.Left) - center.X) / direction.X;
                            double vertical = Math.Abs(direction.Y) < 1e-12 ? double.PositiveInfinity
                                : ((direction.Y > 0 ? bounds.Bottom : bounds.Top) - center.Y) / direction.Y;
                            return Math.Min(horizontal, vertical);
                        }
                        Assert.Equal(6, ((Vector)(center - axis.Start)).Length - DistanceToEdge(-ray), 10);
                        Assert.Equal(20, ((Vector)(axis.End - center)).Length - DistanceToEdge(ray), 10);
                    }
                    if (reference)
                    {
                        Assert.Equal(new[] { SectionAxisDesignation.Y, SectionAxisDesignation.Z }, layout.Axes.Select(a => a.Axis));
                        var y = layout.Axes[0]; var z = layout.Axes[1];
                        Assert.Equal(center.Y, y.Start.Y, 10); Assert.Equal(center.Y, y.End.Y, 10);
                        Assert.Equal(center.X, z.Start.X, 10); Assert.Equal(center.X, z.End.X, 10);
                    }
                    else if (type == SectionEditorType.Angle)
                        Assert.Equal(new[] { SectionAxisDesignation.U, SectionAxisDesignation.V }, layout.Axes.Select(a => a.Axis));
                }
            }
        }
    }
    [Theory]
    [InlineData(false, null)] [InlineData(false, SectionAxisDesignation.Y)]
    [InlineData(true, null)] [InlineData(true, SectionAxisDesignation.Y)] [InlineData(true, SectionAxisDesignation.Z)]
    public void ProductPreviewAxisDashDotPatternKeepsScreenLengthsAndVisibleDotsAcrossSelection(bool complete, SectionAxisDesignation? selected)
    {
        using var environment = new DesktopControlEnvironment();
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection);
        if (complete) Fill(vm, false);
        var canvas = new SectionPreviewControl { Geometry = vm.Preview, SelectedAxis = selected };
        canvas.Measure(new Size(520, 440)); canvas.Arrange(new Rect(0, 0, 520, 440));
        var drawing = new Avalonia.Media.DrawingGroup();
        using (var context = drawing.Open()) canvas.Render(context);
        IEnumerable<Avalonia.Media.Drawing> Drawings(Avalonia.Media.Drawing item) => item is Avalonia.Media.DrawingGroup group
            ? group.Children.SelectMany(Drawings) : [item];
        var axes = Drawings(drawing).OfType<Avalonia.Media.GeometryDrawing>()
            .Where(d => d.Pen?.DashStyle is not null).ToArray();
        Assert.Equal(2, axes.Length);
        var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, canvas.Bounds.Size, canvas.UnitProfile[QuantityKind.SectionDimension],
            text =>
            {
                var formatted = SchematicText.Format(text, SpanDraft.Desktop.Controls.AppTypography.Typeface, 12, Avalonia.Media.Brushes.Gray);
                return new Size(formatted.Width, formatted.Height);
            });
        Rect LineBounds(Point start, Point end) => new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        bool SameBounds(Rect actual, Rect expected) => Math.Abs(actual.X - expected.X) < 1e-4
            && Math.Abs(actual.Y - expected.Y) < 1e-4 && Math.Abs(actual.Width - expected.Width) < 1e-4
            && Math.Abs(actual.Height - expected.Height) < 1e-4;
        foreach (var axis in layout.Axes)
        {
            Vector direction = axis.End - axis.Start; direction /= direction.Length;
            Point solidStart = axis.End - direction * 26;
            var solidBounds = LineBounds(solidStart, axis.End);
            var solid = Assert.Single(Drawings(drawing).OfType<Avalonia.Media.GeometryDrawing>(), d =>
                d.Brush is null && d.Pen is { DashStyle: null } && SameBounds(d.Geometry!.Bounds, solidBounds));
            Assert.Equal(!axis.IsReference && axis.Axis == selected ? 1.6 : .9, solid.Pen!.Thickness);
            Assert.Contains(axes, d => SameBounds(d.Geometry!.Bounds, LineBounds(axis.Start, solidStart)));
        }
        foreach (var axis in axes)
        {
            var pen = axis.Pen!;
            var dashes = Assert.IsAssignableFrom<IReadOnlyList<double>>(pen.DashStyle!.Dashes);
            Assert.Equal(Avalonia.Media.PenLineCap.Round, pen.LineCap);
            Assert.Equal(4, dashes.Count);
            Assert.All(dashes, dash => Assert.True(dash >= 0));
            Assert.Equal(20, dashes[0] * pen.Thickness + pen.Thickness, 10);
            Assert.Equal(4, dashes[1] * pen.Thickness - pen.Thickness, 10);
            Assert.Equal(1.6, dashes[2] * pen.Thickness + pen.Thickness, 10);
            Assert.Equal(4, dashes[3] * pen.Thickness - pen.Thickness, 10);
            Assert.Equal(29.6, dashes.Sum() * pen.Thickness, 10);
            if (!complete)
                Assert.Equal(.9, pen.Thickness); // Reference axes cannot be selected.
        }
        if (complete && selected.HasValue)
            Assert.Single(axes, axis => axis.Pen!.Thickness == 1.6);
    }
}

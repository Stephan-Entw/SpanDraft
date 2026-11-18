using Avalonia.Controls;

namespace SpanDraft.Desktop.Controls;

/// <summary>
/// One row of a flyout form: label, field and an optional unit. Inside a <see cref="Grid.IsSharedSizeScopeProperty"/> scope the
/// label and field columns have the same width in every row, so the labels of a flyout line up and the width follows the
/// longest translated label or option instead of a number measured for one language.
/// </summary>
public sealed class FlyoutFormRow : Grid
{
    /// <summary>The narrowest a field gets; the TextBoxes and ComboBoxes of a flyout share this width.</summary>
    public const double MinFieldWidth = 150;

    public FlyoutFormRow()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto");
        ColumnDefinitions[0].SharedSizeGroup = "FlyoutLabel";
        ColumnDefinitions[1].SharedSizeGroup = "FlyoutField";
        ColumnDefinitions[1].MinWidth = MinFieldWidth;
        ColumnDefinitions[2].SharedSizeGroup = "FlyoutUnit";
    }
}

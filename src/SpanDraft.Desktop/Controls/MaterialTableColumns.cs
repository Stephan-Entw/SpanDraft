using Avalonia.Controls;

namespace SpanDraft.Desktop.Controls;

/// <summary>One column contract for the fixed table heading and every native selection row.</summary>
public sealed class MaterialTableColumns : Grid
{
    public MaterialTableColumns()
    {
        ColumnDefinitions = new("*,120,80,120");
        ColumnDefinitions[0].MinWidth = 200;
        ColumnSpacing = 8;
    }
}

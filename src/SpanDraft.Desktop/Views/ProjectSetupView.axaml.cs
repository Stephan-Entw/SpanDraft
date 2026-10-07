using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.Views;

public partial class ProjectSetupView : UserControl
{
    public ProjectSetupView()
    {
        InitializeComponent();
        // Detaching the setup clears the ComboBox selection and builds an empty item.
        SectionPicker.ItemTemplate = new FuncDataTemplate<Section>((section, _) =>
            new TextBlock { Text = section is null ? null : SectionDisplay.Name(section) });
    }
}

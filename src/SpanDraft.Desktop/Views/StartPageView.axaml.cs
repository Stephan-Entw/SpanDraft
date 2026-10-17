using Avalonia.Controls;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class StartPageView : UserControl
{
    public StartPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as StartPageViewModel)?.Refresh();
    }
}

using Avalonia.Controls;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}

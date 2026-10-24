using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class MaterialEditorWindow : Window
{
    private bool _completed;

    public MaterialEditorWindow() => InitializeComponent();

    public MaterialEditorWindow(MaterialEditorViewModel model) : this()
    {
        DataContext = model;
        model.CloseRequested += Finish;
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Width = Math.Min(Width, screen.WorkingArea.Width / screen.Scaling);
                MaxHeight = screen.WorkingArea.Height / screen.Scaling;
            }
            Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.Name == "MaterialNameInput")?.Focus(NavigationMethod.Tab));
        };
        Closing += (_, e) =>
        {
            if (_completed) return;
            if (model.IsBusy) { e.Cancel = true; return; }
            _completed = true;
            model.CancelCommand.Execute(null);
        };
        Closed += (_, _) => model.CloseRequested -= Finish;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            model.CancelCommand.Execute(null);
        }, RoutingStrategies.Bubble);
    }

    private void Finish(bool accepted)
    {
        if (_completed) return;
        _completed = true;
        Close(accepted);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class ProjectOverviewView : UserControl
{
    public ProjectOverviewView() => InitializeComponent();

    private void EntityNameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel editor || editor.PreserveDrafts || sender is not Button button) return;
        Guid? id = button.DataContext switch
        {
            ProjectOverviewItem item => item.Id,
            ProjectOverviewReaction reaction => reaction.Id,
            _ => null
        };
        if (id is not { } entityId) return;
        // Resolve by identity in the current document, never by the displayed name/type.
        Func<Guid, bool>? edit = editor.Document.Supports.Any(s => s.Id == entityId) ? editor.EditSupport
            : editor.Document.Loads.Any(l => l.Id == entityId) ? editor.EditLoad
            : editor.Document.DistributedLoads.Any(l => l.Id == entityId) ? editor.EditDistributedLoad : null;
        if (edit is null) return;
        bool alreadyEditing = editor.IsSupportFlyoutVisible && editor.SupportDraft?.OriginalId == entityId
            || editor.IsLoadFlyoutVisible && editor.LoadDraft?.OriginalId == entityId
            || editor.IsDistributedLoadFlyoutVisible && editor.DistributedLoadDraft?.OriginalId == entityId;
        if (!alreadyEditing) editor.CancelEditorInteraction();
        edit(entityId);
        e.Handled = true;
    }
}

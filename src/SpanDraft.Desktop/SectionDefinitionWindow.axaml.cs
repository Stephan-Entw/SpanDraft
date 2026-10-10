using Avalonia.Controls;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class SectionDefinitionWindow : Window
{
    public SectionDefinitionWindow() : this(new SectionDefinitionViewModel()) { }
    public SectionDefinitionWindow(SectionDefinitionViewModel model)
    {
        InitializeComponent(); DataContext=model;
        model.CloseRequested += CloseWithResult;
        Closing += (_,e) => { if (model.IsBusy) e.Cancel=true; };
        Closed += (_,_) => model.CloseRequested -= CloseWithResult;
        Opened += (_,_) =>
        {
            if (Screens.ScreenFromWindow(this) is not { } screen) return;
            double width=screen.WorkingArea.Width/screen.Scaling, height=screen.WorkingArea.Height/screen.Scaling;
            MinWidth=Math.Min(MinWidth,width); MinHeight=Math.Min(MinHeight,height);
            Width=Math.Min(Width,width); Height=Math.Min(Height,height);
        };
    }
    private void CloseWithResult(Sections.SectionDefinitionResult? result) => Close(result);
}

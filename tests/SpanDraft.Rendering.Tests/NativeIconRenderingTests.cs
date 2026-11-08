using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class NativeIconRenderingTests : DesktopIconTestSupport
{

    [Fact]
    public void ForegroundIsInheritedAndChangesOnExistingNativeIcons()
    {
        using var environment = new DesktopControlEnvironment();
        var icon = Icon("Info");
        var parent = new ContentControl { Content = icon, Foreground = Brushes.Red };
        var window = new Window { Content = parent };
        window.Show();
        Render(parent, null, 16, 16);
        var path = Assert.Single(icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
        Assert.Same(Brushes.Red, path.Fill);
        parent.Foreground = Brushes.Blue;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Blue, path.Fill);
        icon.Foreground = Brushes.Green;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Green, path.Fill);
        Application.Current!.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["IconTestBrush"] = Brushes.Red };
        Application.Current.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["IconTestBrush"] = Brushes.Blue };
        icon.Bind(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("IconTestBrush"));
        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Red, path.Fill);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Blue, path.Fill);
        window.Close();
    }
}

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using SpanDraft.Desktop;

namespace SpanDraft.Tests;

/// <summary>Real Avalonia controls and styles with inert platform services; no native windows or file I/O.</summary>
public sealed class DesktopControlEnvironment : IDisposable
{
    // Avalonia 12 hides platform service registration from its public reference assemblies.
    // Access it only here so the product needs no test hooks or headless package.
    private readonly IDisposable _scope = (IDisposable)typeof(AvaloniaLocator).GetMethod("EnterScope")!.Invoke(null, null)!;
    private readonly IResourceDictionary _previousResources = Application.Current!.Resources;
    private readonly IStyle[] _styles;

    public DesktopControlEnvironment()
    {
        Bind(DispatchProxy.Create<ICursorFactory, CursorFactoryProxy>());
        Bind((IRenderLoop)typeof(RenderLoop).GetMethod("FromTimer")!.Invoke(null,
            [DispatchProxy.Create<IRenderTimer, DefaultPlatformProxy>()])!);
        Bind(DispatchProxy.Create<IPlatformIconLoader, DefaultPlatformProxy>());
        Bind(DispatchProxy.Create<IWindowingPlatform, WindowingPlatformProxy>());
        var app = new App();
        app.Initialize();
        var resources = app.Resources;
        app.Resources = new ResourceDictionary();
        Application.Current!.Resources = resources;
        _styles = app.Styles.ToArray();
        app.Styles.Clear();
        Application.Current.Styles.AddRange(_styles);
    }

    public void Dispose()
    {
        foreach (var style in _styles) Application.Current!.Styles.Remove(style);
        Application.Current!.Resources = _previousResources;
        _scope.Dispose();
    }

    private static void Bind<T>(T service)
    {
        var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable")!.GetValue(null)!;
        var registration = locator.GetType().GetMethods()
            .Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(T)).Invoke(locator, null)!;
        registration.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(T)).Invoke(registration, [service]);
    }

    public class DefaultPlatformProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.ReturnType.IsValueType && method.ReturnType != typeof(void)
                ? Activator.CreateInstance(method.ReturnType) : null;
    }

    public class WindowingPlatformProxy : DefaultPlatformProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "CreateWindow"
            ? DispatchProxy.Create<IWindowImpl, WindowPlatformProxy>() : base.Invoke(method, args);
    }

    public class CursorFactoryProxy : DefaultPlatformProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            DispatchProxy.Create<ICursorImpl, DefaultPlatformProxy>();
    }

    public class WindowPlatformProxy : DefaultPlatformProxy
    {
        private Compositor? _compositor;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_Compositor" => _compositor ??= (Compositor)Activator.CreateInstance(typeof(Compositor), [null, false])!,
            "get_ClientSize" => new Size(1250, 800),
            "get_RenderScaling" or "get_DesktopScaling" => 1d,
            "get_Surfaces" => Array.Empty<object>(),
            _ => base.Invoke(method, args)
        };
    }
}

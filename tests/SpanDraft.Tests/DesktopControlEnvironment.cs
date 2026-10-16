using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;
using SpanDraft.Desktop;

namespace SpanDraft.Tests;

/// <summary>Real Avalonia controls and styles with inert platform services; no native windows or file I/O.</summary>
public sealed class DesktopControlEnvironment : IDisposable
{
    // Avalonia 12 hides platform service registration from its public reference assemblies.
    // Access it only here so the product needs no test hooks or headless package.
    private readonly IDisposable _scope = (IDisposable)typeof(AvaloniaLocator).GetMethod("EnterScope")!.Invoke(null, null)!;

    public DesktopControlEnvironment(double renderScaling = 1)
    {
        // This collection is serialized, but xUnit can choose a different worker for
        // each test. Give each inert window environment its own owning dispatcher.
        typeof(Dispatcher).GetMethod("ResetBeforeUnitTests", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        Dispatcher.UIThread.VerifyAccess();
        Bind(DispatchProxy.Create<ICursorFactory, CursorFactoryProxy>());
        Bind((IRenderLoop)typeof(RenderLoop).GetMethod("FromTimer")!.Invoke(null,
            [DispatchProxy.Create<IRenderTimer, DefaultPlatformProxy>()])!);
        Bind(DispatchProxy.Create<IPlatformIconLoader, DefaultPlatformProxy>());
        var windowing = DispatchProxy.Create<IWindowingPlatform, WindowingPlatformProxy>();
        ((WindowingPlatformProxy)windowing).RenderScaling = renderScaling;
        Bind(windowing);
        Bind((IPlatformSettings)Activator.CreateInstance(typeof(DefaultPlatformSettings), nonPublic: true)!);
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var hotkeys = (PlatformHotkeyConfiguration)Activator.CreateInstance(typeof(PlatformHotkeyConfiguration),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [primary, OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control, KeyModifiers.Shift], null)!;
        var redo = new List<KeyGesture> { new(Key.Z, primary | KeyModifiers.Shift) };
        if (OperatingSystem.IsWindows()) redo.Add(new(Key.Y, KeyModifiers.Control));
        typeof(PlatformHotkeyConfiguration).GetProperty("Redo")!.SetValue(hotkeys, redo);
        Bind(hotkeys);
        Bind((IFocusManager)Activator.CreateInstance(typeof(FocusManager), nonPublic: true)!);
        Bind((IKeyboardDevice)Activator.CreateInstance(typeof(KeyboardDevice), nonPublic: true)!);
        var mediaType = typeof(Dispatcher).Assembly.GetType("Avalonia.Media.MediaContext")!;
        Bind(mediaType, null!);
        var media = mediaType.GetProperty("Instance")!.GetValue(null)!;
        var clock = mediaType.GetProperty("Clock")!.GetValue(media)!;
        Bind(clock.GetType().GetInterfaces().Single(i => i.Name == "IGlobalClock"), clock);
        var app = new App();
        Bind<Application>(app);
        foreach (string service in new[] { "IGlobalStyles", "IGlobalDataTemplates", "IThemeVariantHost" })
            Bind(typeof(Application).GetInterfaces().Single(i => i.Name == service), app);
        app.Initialize();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    private static void Bind<T>(T service) => Bind(typeof(T), service!);

    private static void Bind(Type serviceType, object service)
    {
        var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable")!.GetValue(null)!;
        var registration = locator.GetType().GetMethods()
            .Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(serviceType).Invoke(locator, null)!;
        registration.GetType().GetMethod("ToConstant")!.MakeGenericMethod(serviceType).Invoke(registration, [service]);
    }

    public static bool MoveFocus(InputElement root, IInputElement input, NavigationDirection direction)
    {
        var type = typeof(InputElement).Assembly.GetType("Avalonia.Input.KeyboardNavigationHandler")!;
        var handler = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetMethod("SetOwner")!.Invoke(handler, [root]);
        return (bool)type.GetMethod("Move")!.Invoke(handler, [input, direction, KeyModifiers.None, null])!;
    }

    public class DefaultPlatformProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.ReturnType.IsValueType && method.ReturnType != typeof(void)
                ? Activator.CreateInstance(method.ReturnType) : null;
    }

    public class WindowingPlatformProxy : DefaultPlatformProxy
    {
        public double RenderScaling { get; set; } = 1;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "CreateWindow") return base.Invoke(method, args);
            var window = DispatchProxy.Create<IWindowImpl, WindowPlatformProxy>();
            ((WindowPlatformProxy)window).RenderScaling = RenderScaling;
            return window;
        }
    }

    public class CursorFactoryProxy : DefaultPlatformProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            DispatchProxy.Create<ICursorImpl, DefaultPlatformProxy>();
    }

    public class WindowPlatformProxy : DefaultPlatformProxy
    {
        public double RenderScaling { get; set; } = 1;
        private Compositor? _compositor;
        private Size _clientSize = new(1250, 800);
        private Action? _closed;
        private readonly IScreenImpl _screens = DispatchProxy.Create<IScreenImpl, DefaultPlatformProxy>();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "Resize" && args?[0] is Size size) { _clientSize = size; return null; }
            if (method.Name == "set_Closed") { _closed = (Action?)args?[0]; return null; }
            if (method.Name == "Dispose") { var closed = _closed; _closed = null; closed?.Invoke(); return null; }
            return method.Name == "TryGetFeature"
                && args?[0] is Type type && type == typeof(IScreenImpl) ? _screens : method.Name switch
            {
                "get_Compositor" => _compositor ??= (Compositor)Activator.CreateInstance(typeof(Compositor), [null, false])!,
                "get_ClientSize" => _clientSize,
                "get_RenderScaling" or "get_DesktopScaling" => RenderScaling,
                "get_Surfaces" => Array.Empty<object>(),
                _ => base.Invoke(method, args)
            };
        }
    }
}

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace SpanDraft.Desktop.Activation;

/// <summary>Maps macOS bundle activations (open file, open URI, reopen) to the shared LaunchRequest model.</summary>
public static class MacActivationSource
{
    public static IReadOnlyList<LaunchRequest> ToRequests(ActivatedEventArgs args)
    {
        switch (args)
        {
            case FileActivatedEventArgs file:
                return [.. file.Files.Select(static item => item.TryGetLocalPath())
                    .Where(static path => path is not null && string.Equals(Path.GetExtension(path), LaunchRequest.ProjectExtension, StringComparison.OrdinalIgnoreCase))
                    .Select(static path => LaunchRequest.OpenProject(path!)).Where(static r => r.IsValid)];
            case ProtocolActivatedEventArgs protocol:
                return LaunchRequest.OpenUri(protocol.Uri.OriginalString) is { IsValid: true } uri ? [uri] : [];
            default:
                return args.Kind is ActivationKind.Reopen ? [LaunchRequest.Activate] : [];
        }
    }

    public static void Attach(Avalonia.Application application, ActivationDispatcher dispatcher)
    {
        if (application.TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime lifetime) return;
        lifetime.Activated += (_, args) =>
        {
            foreach (var request in ToRequests(args)) dispatcher.Enqueue(request);
        };
    }
}

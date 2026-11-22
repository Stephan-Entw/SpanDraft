using System.IO.Pipes;

namespace SpanDraft.Desktop.Activation;

/// <summary>
/// Minimal IPC endpoint owned by the Primary. It needs no Avalonia, settings or workspace and answers
/// every valid request with <c>Retry</c> until a real handler is installed.
/// </summary>
public sealed class BrokerServer : IDisposable
{
    private readonly InstanceMetadata _metadata;
    private readonly CancellationTokenSource _stop = new();
    private volatile Func<LaunchRequest, BrokerStatus> _handler = static _ => BrokerStatus.Retry;
    private readonly Task _loop;
    private int _disposed;

    private BrokerServer(InstanceMetadata metadata)
    {
        _metadata = metadata;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public static BrokerServer Start(InstanceMetadata metadata) => new(metadata);

    /// <summary>The Starting responder is the default; the host replaces it once it can decide on requests.</summary>
    public Func<LaunchRequest, BrokerStatus> Handler { get => _handler; set => _handler = value; }

    private async Task AcceptLoopAsync()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new(_metadata.Endpoint, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                _ = ServeAsync(pipe, token);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); return; }
            catch (IOException) { pipe?.Dispose(); await Task.Delay(50, CancellationToken.None); }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using (pipe)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                string? line = await BrokerProtocol.ReadLineAsync(pipe, timeout.Token);
                var request = line is null ? null : BrokerProtocol.DecodeRequest(line, _metadata.DataScopeId);
                var status = request is null ? BrokerStatus.Rejected : _handler(request);
                await BrokerProtocol.WriteLineAsync(pipe, BrokerProtocol.EncodeResponse(status), timeout.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _stop.Dispose();
    }
}

public static class BrokerClient
{
    /// <summary>Delivers one request; null when the endpoint is missing, unreachable or answers nonsense.</summary>
    public static async Task<BrokerStatus?> SendAsync(InstanceMetadata metadata, LaunchRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", metadata.Endpoint, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(linked.Token);
            await BrokerProtocol.WriteLineAsync(pipe, BrokerProtocol.EncodeRequest(metadata.DataScopeId, request), linked.Token);
            string? line = await BrokerProtocol.ReadLineAsync(pipe, linked.Token);
            return line is null ? null : BrokerProtocol.DecodeResponse(line);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}

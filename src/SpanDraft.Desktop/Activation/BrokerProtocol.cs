using System.Text.Json;

namespace SpanDraft.Desktop.Activation;

public enum BrokerStatus { Accepted, Retry, Rejected }

/// <summary>Deliberately small wire format: one JSON line per request and per response.</summary>
internal static class BrokerProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 16 * 1024;

    private sealed record RequestWire(int V, string? Scope, string? Type, string? Value);
    private sealed record ResponseWire(string? Status);

    public static string EncodeRequest(string dataScopeId, LaunchRequest request) =>
        JsonSerializer.Serialize(new RequestWire(Version, dataScopeId, request.Type.ToString(), request.Value));

    /// <summary>Returns null for anything that is not a valid request of this protocol version and data scope.</summary>
    public static LaunchRequest? DecodeRequest(string line, string expectedScope)
    {
        try
        {
            var wire = JsonSerializer.Deserialize<RequestWire>(line);
            if (wire is not { V: Version } || wire.Scope != expectedScope
                || !Enum.TryParse<LaunchRequestType>(wire.Type, out var type) || !Enum.IsDefined(type)) return null;
            var request = new LaunchRequest(type, wire.Value);
            return request.IsValid ? request : null;
        }
        catch (JsonException) { return null; }
    }

    public static string EncodeResponse(BrokerStatus status) =>
        JsonSerializer.Serialize(new ResponseWire(status.ToString()));

    public static BrokerStatus? DecodeResponse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<ResponseWire>(line) is { Status: { } text }
                && Enum.TryParse<BrokerStatus>(text, out var status) && Enum.IsDefined(status) ? status : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Reads one bounded line; null when the peer closed early or exceeded the limit.</summary>
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count <= MaxMessageBytes)
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0) return null;
            if (one[0] == (byte)'\n') return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(one[0]);
        }
        return null;
    }

    public static async Task WriteLineAsync(Stream stream, string line, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

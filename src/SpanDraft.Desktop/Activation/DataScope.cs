using System.Text.Json;

namespace SpanDraft.Desktop.Activation;

/// <summary>The app data area whose single Primary is elected; Election lock and Instance metadata live inside it.</summary>
public sealed class DataScope(string directory)
{
    public string Directory { get; } = Path.GetFullPath(directory);
    public string LockPath => Path.Combine(Directory, "instance.lock");
    public string MetadataPath => Path.Combine(Directory, "instance.json");

    public static DataScope Local() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft"));
}

/// <summary>The only data a Secondary may read: stable scope identity and the IPC endpoint derived from it.</summary>
public sealed record InstanceMetadata(string DataScopeId)
{
    public string Endpoint => "SpanDraft-" + DataScopeId;

    public static InstanceMetadata? TryRead(DataScope scope)
    {
        try
        {
            using var stream = new FileStream(scope.MetadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var wire = JsonSerializer.Deserialize<Wire>(stream);
            return wire?.DataScopeId is { Length: > 0 } id && id.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-')
                ? new(id) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Atomic first creation; callers must hold the Election lock.</summary>
    internal static InstanceMetadata ReadOrCreate(DataScope scope)
    {
        if (TryRead(scope) is { } existing) return existing;
        var created = new InstanceMetadata(Guid.NewGuid().ToString("N"));
        string temp = scope.MetadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Wire(created.DataScopeId)));
        File.Move(temp, scope.MetadataPath, overwrite: true);
        return created;
    }

    private sealed record Wire(string? DataScopeId);
}

/// <summary>Proof of a won election: the exclusively held lock file plus the metadata created or read under it.</summary>
public sealed class PrimaryInstance : IDisposable
{
    private readonly FileStream _lock;

    private PrimaryInstance(FileStream lockStream, InstanceMetadata metadata)
    {
        _lock = lockStream;
        Metadata = metadata;
    }

    public InstanceMetadata Metadata { get; }

    /// <summary>The operating system releases the exclusive lock on process exit, including crashes.</summary>
    public static PrimaryInstance? TryAcquire(DataScope scope)
    {
        System.IO.Directory.CreateDirectory(scope.Directory);
        FileStream stream;
        try { stream = new(scope.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
        try { return new(stream, InstanceMetadata.ReadOrCreate(scope)); }
        catch { stream.Dispose(); throw; }
    }

    public void Dispose() => _lock.Dispose();
}

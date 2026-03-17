using System.Diagnostics;
using System.Text.Json;

namespace TeamsRelay.Core;

public sealed class RelayStateStore
{
    private readonly RelayRuntimePaths paths;

    public RelayStateStore(RelayRuntimePaths paths)
    {
        this.paths = paths;
    }

    public RelayRuntimePaths Paths => paths;

    public RelayStateSnapshot Read()
    {
        paths.EnsureDirectories();

        var processId = ReadProcessId();
        var metadata = ReadMetadata();

        if (processId is null)
        {
            return new RelayStateSnapshot
            {
                ProcessState = RelayProcessState.Stopped,
                Metadata = metadata
            };
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (!process.HasExited && MatchesMetadata(process, metadata))
            {
                return new RelayStateSnapshot
                {
                    ProcessState = RelayProcessState.Running,
                    ProcessId = processId,
                    Metadata = metadata
                };
            }
        }
        catch (Exception)
        {}

        return new RelayStateSnapshot
        {
            ProcessState = RelayProcessState.Stale,
            ProcessId = processId,
            Metadata = metadata
        };
    }

    public void Clear()
    {
        File.Delete(paths.PidFilePath);
        File.Delete(paths.MetadataFilePath);
        File.Delete(paths.StopFilePath);
    }

    public async Task WriteAsync(RelayInstanceMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.PidFilePath, metadata.ProcessId.ToString(), cancellationToken);
        var json = JsonSerializer.Serialize(metadata, JsonDefaults.Indented) + Environment.NewLine;
        await File.WriteAllTextAsync(paths.MetadataFilePath, json, cancellationToken);
    }

    private RelayInstanceMetadata? ReadMetadata()
    {
        if (!File.Exists(paths.MetadataFilePath))
        {
            return null;
        }

        try
        {
            var raw = File.ReadAllText(paths.MetadataFilePath);
            return JsonSerializer.Deserialize<RelayInstanceMetadata>(raw, JsonDefaults.Indented);
        }
        catch (Exception)
        {
        return null;
        }
    }

    private int? ReadProcessId()
    {
        if (!File.Exists(paths.PidFilePath))
        {
            return null;
        }

        var raw = File.ReadAllText(paths.PidFilePath).Trim();
        return int.TryParse(raw, out var processId) ? processId : null;
    }

    private static bool MatchesMetadata(Process process, RelayInstanceMetadata? metadata)
    {
        if (metadata is null || metadata.StartedAtUtc == default)
        {
            return false;
        }

        try
        {
            var processStart = new DateTimeOffset(process.StartTime);
            return (processStart - metadata.StartedAtUtc).Duration() <= TimeSpan.FromMinutes(2);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

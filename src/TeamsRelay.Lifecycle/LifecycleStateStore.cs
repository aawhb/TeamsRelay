using System.Text.Json;
using TeamsRelay.Core;

namespace TeamsRelay.Lifecycle;

public sealed class LifecycleStateStore
{
    private readonly RuntimePaths _paths;
    private readonly IProcessLauncher _processLauncher;

    public LifecycleStateStore(RuntimePaths paths, IProcessLauncher? processLauncher = null)
    {
        _paths = paths;
        _processLauncher = processLauncher ?? SystemProcessLauncher.Instance;
    }

    public RuntimePaths Paths => _paths;

    public LifecycleStatus Read()
    {
        _paths.EnsureDirectories();

        var processId = ReadProcessId();
        var metadata = ReadMetadata();

        if (processId is null)
        {
            return new LifecycleStatus
            {
                ProcessState = LifecycleState.Stopped,
                Metadata = metadata
            };
        }

        if (_processLauncher.IsRunning(processId.Value) && MatchesMetadata(processId.Value, metadata))
        {
            return new LifecycleStatus
            {
                ProcessState = LifecycleState.Running,
                ProcessId = processId,
                Metadata = metadata
            };
        }

        return new LifecycleStatus
        {
            ProcessState = LifecycleState.Stale,
            ProcessId = processId,
            Metadata = metadata
        };
    }

    public void Clear()
    {
        File.Delete(_paths.PidFilePath);
        File.Delete(_paths.MetadataFilePath);
        File.Delete(_paths.StopFilePath);
    }

    public async Task WriteAsync(InstanceMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        _paths.EnsureDirectories();
        await File.WriteAllTextAsync(_paths.PidFilePath, metadata.ProcessId.ToString(), cancellationToken);
        var json = JsonSerializer.Serialize(metadata, JsonDefaults.Indented) + Environment.NewLine;
        await File.WriteAllTextAsync(_paths.MetadataFilePath, json, cancellationToken);
    }

    private InstanceMetadata? ReadMetadata()
    {
        if (!File.Exists(_paths.MetadataFilePath))
        {
            return null;
        }

        try
        {
            var raw = File.ReadAllText(_paths.MetadataFilePath);
            return JsonSerializer.Deserialize<InstanceMetadata>(raw, JsonDefaults.Indented);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private int? ReadProcessId()
    {
        if (!File.Exists(_paths.PidFilePath))
        {
            return null;
        }

        var raw = File.ReadAllText(_paths.PidFilePath).Trim();
        return int.TryParse(raw, out var processId) ? processId : null;
    }

    private bool MatchesMetadata(int processId, InstanceMetadata? metadata)
    {
        if (metadata is null || metadata.StartedAtUtc == default)
        {
            return false;
        }

        if (!_processLauncher.TryGetStartTime(processId, out var processStart))
        {
            return false;
        }

        return (processStart - metadata.StartedAtUtc).Duration() <= TimeSpan.FromMinutes(2);
    }
}

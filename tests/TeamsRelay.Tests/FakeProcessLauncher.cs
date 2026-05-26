using System.Diagnostics;
using TeamsRelay.Lifecycle;

namespace TeamsRelay.Tests;

public sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly Dictionary<int, FakeProcessRecord> _live = new();
    private int _nextSpawnId = 1000;

    public int CurrentProcessId { get; set; } = 1;

    public List<int> KilledProcessIds { get; } = new();

    public List<ProcessStartInfo> SpawnedStartInfos { get; } = new();

    public Func<ProcessStartInfo, FakeProcessRecord>? SpawnFactory { get; set; }

    public IProcessHandle Spawn(ProcessStartInfo startInfo)
    {
        SpawnedStartInfos.Add(startInfo);
        var record = SpawnFactory?.Invoke(startInfo)
            ?? new FakeProcessRecord(_nextSpawnId++, DateTimeOffset.UtcNow);
        _live[record.ProcessId] = record;
        return new FakeProcessHandle(record);
    }

    public bool IsRunning(int processId)
        => _live.TryGetValue(processId, out var record) && !record.HasExited;

    public void Kill(int processId)
    {
        KilledProcessIds.Add(processId);
        Exit(processId, exitCode: -1);
    }

    public bool TryGetStartTime(int processId, out DateTimeOffset startTimeUtc)
    {
        if (_live.TryGetValue(processId, out var record))
        {
            startTimeUtc = record.StartTimeUtc;
            return true;
        }
        startTimeUtc = default;
        return false;
    }

    public FakeProcessRecord Seed(int processId, DateTimeOffset startTimeUtc)
    {
        var record = new FakeProcessRecord(processId, startTimeUtc);
        _live[processId] = record;
        return record;
    }

    public void Exit(int processId, int exitCode = 0)
    {
        if (_live.TryGetValue(processId, out var record))
        {
            record.MarkExited(exitCode);
        }
    }
}

public sealed class FakeProcessRecord
{
    public FakeProcessRecord(int processId, DateTimeOffset startTimeUtc)
    {
        ProcessId = processId;
        StartTimeUtc = startTimeUtc;
    }

    public int ProcessId { get; }

    public DateTimeOffset StartTimeUtc { get; }

    public bool HasExited { get; private set; }

    public int ExitCode { get; private set; }

    public void MarkExited(int exitCode)
    {
        HasExited = true;
        ExitCode = exitCode;
    }
}

internal sealed class FakeProcessHandle : IProcessHandle
{
    private readonly FakeProcessRecord _record;

    public FakeProcessHandle(FakeProcessRecord record)
    {
        _record = record;
    }

    public int Id => _record.ProcessId;

    public bool HasExited => _record.HasExited;

    public int ExitCode => _record.ExitCode;

    public void Refresh() { }

    public void Dispose() { }
}

using TeamsRelay.Core;
using System.Diagnostics;

namespace TeamsRelay.Tests;

public sealed class RelayStateStoreTests
{
    [Fact]
    public void ReadReturnsStoppedWithoutStateFiles()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var store = new RelayStateStore(RelayRuntimePaths.Create(new AppEnvironment(root)));

        var snapshot = store.Read();

        Assert.Equal(RelayProcessState.Stopped, snapshot.ProcessState);
        Assert.Null(snapshot.ProcessId);
    }

    [Fact]
    public async Task ReadReturnsStaleForMissingProcess()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var store = new RelayStateStore(RelayRuntimePaths.Create(environment));

        await store.WriteAsync(new RelayInstanceMetadata
        {
            ProcessId = int.MaxValue,
            StartedAtUtc = DateTimeOffset.UtcNow,
            ConfigPath = environment.DefaultConfigPath,
            Version = ApplicationVersion.Value
        });

        var snapshot = store.Read();

        Assert.Equal(RelayProcessState.Stale, snapshot.ProcessState);
        Assert.Equal(int.MaxValue, snapshot.ProcessId);
    }

    [Fact]
    public async Task ReadTreatsMismatchedLiveProcessAsStale()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var store = new RelayStateStore(RelayRuntimePaths.Create(environment));

        await store.WriteAsync(new RelayInstanceMetadata
        {
            ProcessId = Process.GetCurrentProcess().Id,
            StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ConfigPath = environment.DefaultConfigPath,
            Version = ApplicationVersion.Value
        });

        var snapshot = store.Read();

        Assert.Equal(RelayProcessState.Stale, snapshot.ProcessState);
        Assert.Equal(Process.GetCurrentProcess().Id, snapshot.ProcessId);
    }
}

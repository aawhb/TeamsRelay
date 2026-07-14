using TeamsRelay.Core;
using TeamsRelay.Lifecycle;
using System.Diagnostics;

namespace TeamsRelay.Tests;

public sealed class LifecycleStateStoreTests
{
    [Fact]
    public void ReadReturnsStoppedWithoutStateFiles()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var store = new LifecycleStateStore(RuntimePaths.Create(new AppEnvironment(root)));

        var snapshot = store.Read();

        Assert.Equal(LifecycleState.Stopped, snapshot.ProcessState);
        Assert.Null(snapshot.ProcessId);
    }

    [Fact]
    public async Task ReadReturnsStaleForMissingProcess()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var store = new LifecycleStateStore(RuntimePaths.Create(environment));

        await store.WriteAsync(new InstanceMetadata
        {
            ProcessId = int.MaxValue,
            StartedAtUtc = DateTimeOffset.UtcNow,
            ConfigPath = environment.DefaultConfigPath,
            Version = ApplicationVersion.Value
        });

        var snapshot = store.Read();

        Assert.Equal(LifecycleState.Stale, snapshot.ProcessState);
        Assert.Equal(int.MaxValue, snapshot.ProcessId);
    }

    [Fact]
    public async Task ReadTreatsMismatchedLiveProcessAsStale()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var store = new LifecycleStateStore(RuntimePaths.Create(environment));

        await store.WriteAsync(new InstanceMetadata
        {
            ProcessId = Process.GetCurrentProcess().Id,
            StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ConfigPath = environment.DefaultConfigPath,
            Version = ApplicationVersion.Value
        });

        var snapshot = store.Read();

        Assert.Equal(LifecycleState.Stale, snapshot.ProcessState);
        Assert.Equal(Process.GetCurrentProcess().Id, snapshot.ProcessId);
    }

    [Fact]
    public void ReadTreatsMalformedMetadataAsMissing()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var paths = RuntimePaths.Create(new AppEnvironment(root));
        paths.EnsureDirectories();
        File.WriteAllText(paths.MetadataFilePath, "not json");
        var store = new LifecycleStateStore(paths);

        var snapshot = store.Read();

        Assert.Equal(LifecycleState.Stopped, snapshot.ProcessState);
        Assert.Null(snapshot.Metadata);
    }
}

using System.Diagnostics;
using System.Reflection;
using TeamsRelay.App;
using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task HelpPrintsUsage()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("teamsrelay run", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task VersionPrintsVersion()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["--version"]);

        Assert.Equal(0, exitCode);
        Assert.Equal($"teamsrelay {ApplicationVersion.Value}{Environment.NewLine}", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task ConfigInitHelpPrintsUsage()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["config", "init", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("teamsrelay config init", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task ConfigInitWritesDefaultConfigInWorkingRoot()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr, new AppEnvironment(root));

        var exitCode = await app.RunAsync(["config", "init"]);

        Assert.Equal(0, exitCode);
        Assert.Contains(Path.Combine(root, "config", "relay.config.json"), stdout.ToString());
        Assert.True(File.Exists(Path.Combine(root, "config", "relay.config.json")));
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task StatusPrintsStoppedWhenNoRunnerExists()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr, new AppEnvironment(root));

        var exitCode = await app.RunAsync(["status"]);

        Assert.Equal(0, exitCode);
        Assert.Equal($"Status: stopped{Environment.NewLine}", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task DevicesHelpPrintsUsage()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["devices", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("teamsrelay devices", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task RunHelpPrintsUsage()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["run", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("teamsrelay run", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task StopHelpPrintsUsage()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["stop", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("teamsrelay stop", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task StopPrintsNotRunningWhenNothingCanBeStopped()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["stop"]);

        Assert.Equal(0, exitCode);
        Assert.Equal($"Relay is not running.{Environment.NewLine}", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Empty(processOperations.KilledProcessIds);
    }

    [Fact]
    public async Task StopTerminatesAuxiliaryRepoLocalProcessWhenNoTrackedRelayExists()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        processOperations.Processes.Add(new LocalProcessInfo(
            42,
            "TeamsRelay.exe",
            Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe"),
            $"\"{Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe")}\" logs --follow"));
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["stop"]);

        Assert.Equal(0, exitCode);
        Assert.Equal($"Stopped 1 local TeamsRelay process.{Environment.NewLine}", stdout.ToString());
        Assert.Equal([42], processOperations.KilledProcessIds);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task StopClearsStaleStateAndStopsAuxiliaryProcesses()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var stateStore = new RelayStateStore(RelayRuntimePaths.Create(new AppEnvironment(root)));
        Directory.CreateDirectory(Path.GetDirectoryName(stateStore.Paths.PidFilePath)!);
        await File.WriteAllTextAsync(stateStore.Paths.PidFilePath, "999999");

        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        processOperations.Processes.Add(new LocalProcessInfo(
            52,
            "TeamsRelay.exe",
            Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe"),
            null));
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["stop"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $"Relay is not running (stale state cleared). Stopped 1 local TeamsRelay process.{Environment.NewLine}",
            stdout.ToString());
        Assert.False(File.Exists(stateStore.Paths.PidFilePath));
        Assert.Equal([52], processOperations.KilledProcessIds);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task StopStopsTrackedRelayAndAuxiliaryProcesses()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var stateStore = new RelayStateStore(RelayRuntimePaths.Create(environment));
        var trackedProcessId = Process.GetCurrentProcess().Id;
        await stateStore.WriteAsync(new RelayInstanceMetadata
        {
            ProcessId = trackedProcessId,
            StartedAtUtc = new DateTimeOffset(Process.GetCurrentProcess().StartTime),
            ConfigPath = environment.DefaultConfigPath,
            Version = ApplicationVersion.Value
        });

        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        processOperations.SetRunningSequence(trackedProcessId, [false]);
        processOperations.Processes.Add(new LocalProcessInfo(
            78,
            "TeamsRelay.exe",
            Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe"),
            null));
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["stop", "--timeout-seconds", "1"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $"Relay stopped. Stopped 1 local TeamsRelay process.{Environment.NewLine}",
            stdout.ToString());
        Assert.False(File.Exists(stateStore.Paths.PidFilePath));
        Assert.False(File.Exists(stateStore.Paths.StopFilePath));
        Assert.Equal([78], processOperations.KilledProcessIds);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task StopExcludesCurrentProcessFromAuxiliaryCleanup()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations { CurrentProcessId = 99 };
        processOperations.Processes.Add(new LocalProcessInfo(99, "TeamsRelay.exe", Path.Combine(root, "TeamsRelay.exe"), null));
        processOperations.Processes.Add(new LocalProcessInfo(100, "TeamsRelay.exe", Path.Combine(root, "other", "TeamsRelay.exe"), null));
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["stop"]);

        Assert.Equal(0, exitCode);
        Assert.Equal([100], processOperations.KilledProcessIds);
        Assert.Equal($"Stopped 1 local TeamsRelay process.{Environment.NewLine}", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task DoctorPassesWhenNoRepoLocalProcesses()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        await InitializeConfig(root);
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["doctor"]);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("[PASS] relay_state:", output);
        Assert.Contains("stopped", output);
        Assert.Contains("[PASS] relay_process_count:", output);
        Assert.Contains("0 repo-local process(es)", output);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task DoctorWarnsWhenMultipleRepoLocalProcesses()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        await InitializeConfig(root);
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        processOperations.Processes.Add(new LocalProcessInfo(10, "TeamsRelay.exe", Path.Combine(root, "TeamsRelay.exe"), null));
        processOperations.Processes.Add(new LocalProcessInfo(20, "TeamsRelay.exe", Path.Combine(root, "other", "TeamsRelay.exe"), null));
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["doctor"]);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("[WARN] relay_process_count:", output);
        Assert.Contains("2 repo-local process(es)", output);
        Assert.Contains("pid=10", output);
        Assert.Contains("pid=20", output);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task DoctorWarnsOnStaleState()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        await InitializeConfig(root);
        var stateStore = new RelayStateStore(RelayRuntimePaths.Create(new AppEnvironment(root)));
        Directory.CreateDirectory(Path.GetDirectoryName(stateStore.Paths.PidFilePath)!);
        await File.WriteAllTextAsync(stateStore.Paths.PidFilePath, "999999");
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var processOperations = new FakeProcessOperations();
        var app = CreateCliApplication(root, stdout, stderr, processOperations);

        var exitCode = await app.RunAsync(["doctor"]);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("[WARN] relay_state:", output);
        Assert.Contains("stale", output);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task UnknownCommandFails()
    {
        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(stdout, stderr);

        var exitCode = await app.RunAsync(["bogus"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command: bogus", stderr.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void ResolveTargetDevicesFailsWhenConfiguredIdIsMissing()
    {
        var app = new CliApplication(new StringReader(string.Empty), new StringWriter(), new StringWriter(), new AppEnvironment(TestHelpers.CreateTemporaryDirectory()));
        var method = typeof(CliApplication).GetMethod("ResolveTargetDevices", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveTargetDevices was not found.");
        var inventory = new[]
        {
            new RelayDevice { Id = "device-1", Name = "Phone", IsAvailable = true }
        };

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(app, [inventory, Array.Empty<string>(), Array.Empty<string>(), new[] { "device-2" }, true]));

        var cliException = Assert.IsType<CliException>(exception.InnerException);
        Assert.Contains("Configured target device id 'device-2' is not paired", cliException.Message);
    }

    [Fact]
    public async Task LogsFollowReturnsCleanlyWhenCancelled()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var logsDirectory = Path.Combine(root, "runtime", "logs");
        Directory.CreateDirectory(logsDirectory);
        await File.WriteAllTextAsync(Path.Combine(logsDirectory, "relay-20260101-000000.log"), "{\"event\":\"startup\"}" + Environment.NewLine);

        var (stdout, stderr) = TestHelpers.CreateWriters();
        var app = new CliApplication(new StringReader(string.Empty), stdout, stderr, environment);

        using var cts = new CancellationTokenSource();
        var runTask = app.RunAsync(["logs", "--follow"], cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Contains("startup", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void RuntimeProcessOperationsMatchesRepoLocalExecutableProcess()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var process = new LocalProcessInfo(
            1,
            "TeamsRelay.exe",
            Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.exe"),
            null);

        var matches = RuntimeProcessOperations.IsRepoLocalTeamsRelayProcess(process, root);

        Assert.True(matches);
    }

    [Fact]
    public void RuntimeProcessOperationsMatchesRepoLocalDotnetHostedProcess()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var dllPath = Path.Combine(root, "src", "TeamsRelay.App", "bin", "Debug", "net9.0-windows", "TeamsRelay.dll");
        var process = new LocalProcessInfo(
            1,
            "dotnet.exe",
            @"C:\Program Files\dotnet\dotnet.exe",
            $"dotnet \"{dllPath}\" logs --follow");

        var matches = RuntimeProcessOperations.IsRepoLocalTeamsRelayProcess(process, root);

        Assert.True(matches);
    }

    [Fact]
    public void RuntimeProcessOperationsIgnoresDotnetProcessOutsideRoot()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var process = new LocalProcessInfo(
            1,
            "dotnet.exe",
            @"C:\Program Files\dotnet\dotnet.exe",
            "dotnet \"D:\\Elsewhere\\TeamsRelay.dll\" logs --follow");

        var matches = RuntimeProcessOperations.IsRepoLocalTeamsRelayProcess(process, root);

        Assert.False(matches);
    }

    private static CliApplication CreateCliApplication(
        string root,
        TextWriter stdout,
        TextWriter stderr,
        IProcessOperations processOperations)
    {
        return new CliApplication(
            new StringReader(string.Empty),
            stdout,
            stderr,
            new AppEnvironment(root),
            new FakeTargetAdapter(),
            processOperations);
    }

    private static async Task InitializeConfig(string root)
    {
        var configFiles = new ConfigFileService(new AppEnvironment(root));
        await configFiles.InitializeAsync(null, force: false, CancellationToken.None);
    }

    private sealed class FakeProcessOperations : IProcessOperations
    {
        private readonly Dictionary<int, Queue<bool>> runningSequences = [];

        public int CurrentProcessId { get; set; } = -1;

        public List<int> KilledProcessIds { get; } = [];

        public List<LocalProcessInfo> Processes { get; } = [];

        public IReadOnlyList<LocalProcessInfo> FindRepoLocalTeamsRelayProcesses(string rootDirectory) => Processes;

        public bool IsRunning(int processId)
        {
            if (runningSequences.TryGetValue(processId, out var sequence) && sequence.Count > 0)
            {
                return sequence.Dequeue();
            }

            return false;
        }

        public void Kill(int processId)
        {
            KilledProcessIds.Add(processId);
        }

        public void SetRunningSequence(int processId, IReadOnlyList<bool> states)
        {
            runningSequences[processId] = new Queue<bool>(states);
        }
    }
}

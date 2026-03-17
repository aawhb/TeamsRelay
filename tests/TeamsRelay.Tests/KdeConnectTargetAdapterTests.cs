using TeamsRelay.Core;
using TeamsRelay.Target.KdeConnect;

namespace TeamsRelay.Tests;

public sealed class KdeConnectTargetAdapterTests
{
    [Fact]
    public async Task InventoryMergesPairedAndAvailableDevices()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var runner = new FakeKdeCommandRunner();
        runner.Add(["-l", "--id-name-only"], 0, """
        abc12345 Phone One
        xyz98765 Tablet One
        """);
        runner.Add(["-a", "--id-name-only"], 0, """
        abc12345 Phone One
        """);

        var adapter = new KdeConnectTargetAdapter(environment, runner);
        var devices = await adapter.GetDeviceInventoryAsync(CreateConfig(root));

        Assert.Collection(
            devices,
            first =>
            {
                Assert.Equal("abc12345", first.Id);
                Assert.True(first.IsAvailable);
            },
            second =>
            {
                Assert.Equal("xyz98765", second.Id);
                Assert.False(second.IsAvailable);
            });
    }

    [Fact]
    public async Task InventoryIgnoresKnownWarningLines()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var runner = new FakeKdeCommandRunner();
        runner.Add(["-l", "--id-name-only"], 0, """
        QEventDispatcherWin32::wakeUp: Failed to post a message (Invalid window handle.)
        abc12345 Phone One
        """);
        runner.Add(["-a", "--id-name-only"], 0, """
        abc12345 Phone One
        """);

        var adapter = new KdeConnectTargetAdapter(environment, runner);
        var devices = await adapter.GetDeviceInventoryAsync(CreateConfig(root));

        Assert.Single(devices);
        Assert.Equal("abc12345", devices[0].Id);
    }

    [Fact]
    public async Task DoctorWarnsWhenNoTargetIsConfigured()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var runner = new FakeKdeCommandRunner();
        runner.Add(["-l", "--id-name-only"], 0, "abc12345 Phone One");
        runner.Add(["-a", "--id-name-only"], 0, "abc12345 Phone One");
        var adapter = new KdeConnectTargetAdapter(environment, runner);

        var report = await adapter.RunDoctorAsync(CreateConfig(root));

        Assert.False(report.HasBlockingFailures);
        Assert.Contains(report.Checks, check => check.Name == "target_selection" && !check.Passed && !check.IsBlocking);
    }

    [Fact]
    public async Task DoctorWarnsForOfflineConfiguredTarget()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var runner = new FakeKdeCommandRunner();
        runner.Add(["-l", "--id-name-only"], 0, "abc12345 Phone One");
        runner.Add(["-a", "--id-name-only"], 0, "");
        var adapter = new KdeConnectTargetAdapter(environment, runner);
        var config = CreateConfig(root, ["abc12345"]);

        var report = await adapter.RunDoctorAsync(config);

        Assert.Contains(report.Checks, check => check.Name == "target_reachable:abc12345" && !check.Passed && !check.IsBlocking);
    }

    [Fact]
    public async Task SendAsyncThrowsPartialFailureWhenOnlySomeTargetsSucceed()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var runner = new FakeKdeCommandRunner();
        runner.Add(["-d", "abc12345", "--ping-msg", "Build complete"], 0, string.Empty);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            runner.Add(["-d", "xyz98765", "--ping-msg", "Build complete"], 1, "device unreachable");
        }
        var adapter = new KdeConnectTargetAdapter(environment, runner);

        var exception = await Assert.ThrowsAsync<PartialTargetSendException>(() => adapter.SendAsync(
            CreateConfig(root),
            ["abc12345", "xyz98765"],
            "Build complete"));

        Assert.Equal(1, exception.SuccessCount);
        Assert.Single(exception.Failures);
        Assert.Contains("xyz98765: device unreachable", exception.Message);
    }

    private static RelayConfig CreateConfig(string root, params string[] deviceIds)
    {
        var toolsDirectory = Path.Combine(root, "tools");
        Directory.CreateDirectory(toolsDirectory);
        var cliPath = Path.Combine(toolsDirectory, "kdeconnect-cli.cmd");
        File.WriteAllText(cliPath, "@echo off");

        return RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Target = new TargetOptions
            {
                Kind = "kde_connect",
                KdeCliPath = Path.Combine("tools", "kdeconnect-cli.cmd"),
                DeviceIds = deviceIds
            }
        });
    }

    private sealed class FakeKdeCommandRunner : IKdeCommandRunner
    {
        private readonly Dictionary<string, Queue<KdeCommandResult>> results = new(StringComparer.Ordinal);

        public void Add(IReadOnlyList<string> arguments, int exitCode, string output)
        {
            var key = Key(arguments);
            if (!results.TryGetValue(key, out var queue))
            {
                queue = new Queue<KdeCommandResult>();
                results[key] = queue;
            }

            queue.Enqueue(new KdeCommandResult
            {
                ExitCode = exitCode,
                OutputLines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            });
        }

        public Task<KdeCommandResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            if (results.TryGetValue(Key(arguments), out var queue) && queue.Count > 0)
            {
                return Task.FromResult(queue.Dequeue());
            }

            throw new InvalidOperationException($"No fake result configured for '{Key(arguments)}'.");
        }

        private static string Key(IReadOnlyList<string> arguments) => string.Join('\u001f', arguments);
    }
}

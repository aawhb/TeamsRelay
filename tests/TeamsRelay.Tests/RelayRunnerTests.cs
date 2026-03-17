using System.Text.Json;
using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class RelayRunnerTests
{
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task RunnerForwardsEventsToTarget()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([
            TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete", processId: 42, topLevelWindowName: "Teams notification")
        ]);
        var target = new FakeTargetAdapter();
        var runner = new RelayRunner(
            environment,
            RelayConfig.CreateDefault(),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1"]);

        await RunUntilAsync(
            runner,
            () => Task.FromResult(target.Messages.Count == 1));

        Assert.Contains("Build complete", target.Messages.Single());
    }

    [Fact]
    public async Task RunnerContinuesWhenTargetSendFails()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([
            TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete", processId: 42, topLevelWindowName: "Teams notification")
        ]);
        var target = new FakeTargetAdapter { ThrowOnSend = true };
        var runner = new RelayRunner(
            environment,
            RelayConfig.CreateDefault(),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1"]);

        var logsDir = Path.Combine(environment.RootDirectory, "runtime", "logs");
        await RunUntilAsync(
            runner,
            async () => (await ReadLogEntriesIfExistsAsync(FindInstanceLogPath(logsDir)))
                .Any(entry => entry.Event == "target_send_failed"));

        var logPath = FindInstanceLogPath(logsDir);
        Assert.True(File.Exists(logPath));
        Assert.Contains("target_send_failed", await File.ReadAllTextAsync(logPath));
    }

    [Fact]
    public async Task RunnerLogsPartialTargetDeliveryFailures()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([
            TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete", processId: 42, topLevelWindowName: "Teams notification")
        ]);
        var target = new FakeTargetAdapter
        {
            PartialFailure = new PartialTargetSendException(1, ["device-2: device unreachable"])
        };
        var runner = new RelayRunner(
            environment,
            RelayConfig.CreateDefault(),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1", "device-2"]);

        var logsDir = Path.Combine(environment.RootDirectory, "runtime", "logs");
        await RunUntilAsync(
            runner,
            async () => (await ReadLogEntriesIfExistsAsync(FindInstanceLogPath(logsDir)))
                .Any(entry => entry.Event == "target_partial_success"));

        var logText = await File.ReadAllTextAsync(FindInstanceLogPath(logsDir));
        Assert.Contains("target_partial_success", logText);
        Assert.DoesNotContain("\"event\":\"forwarded\"", logText);
    }

    [Fact]
    public async Task RunnerWritesCandidateLogsWhenDebugIsEnabled()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([
            TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete", processId: 42, topLevelWindowName: "Teams notification"),
            TestHelpers.CreateTeamsRecord(
                "Message preview | Alex | Build complete",
                capturePath: "window_opened_enriched",
                processId: 42,
                topLevelWindowName: "Teams notification"),
            TestHelpers.CreateTeamsRecord("Thumbnail Preview", processId: 42, topLevelWindowName: "Teams notification")
        ], [
            TestHelpers.CreateDiagnostic("source_event_seen", "raw_capture"),
            TestHelpers.CreateDiagnostic("source_event_dropped", "not_message_like")
        ]);
        var target = new FakeTargetAdapter();
        var runner = new RelayRunner(
            environment,
            CreateConfig("debug"),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1"]);

        var logsDir = Path.Combine(environment.RootDirectory, "runtime", "logs");
        await RunUntilAsync(
            runner,
            async () =>
            {
                var entries = await ReadLogEntriesIfExistsAsync(FindInstanceLogPath(logsDir));
                return entries.Any(entry => entry.Event == "candidate_accepted")
                    && entries.Any(entry => entry.Event == "candidate_merged")
                    && entries.Any(entry => entry.Event == "candidate_rejected")
                    && entries.Any(entry => entry.Event == "source_event_seen")
                    && entries.Any(entry => entry.Event == "source_event_dropped");
            });

        var entries = await ReadLogEntriesAsync(FindInstanceLogPath(logsDir));

        Assert.Contains(entries, entry => entry.Event == "candidate_accepted");
        Assert.Contains(entries, entry => entry.Event == "candidate_merged");
        Assert.Contains(entries, entry => entry.Event == "candidate_rejected");
        Assert.Contains(entries, entry => entry.Event == "source_event_seen");
        Assert.Contains(entries, entry => entry.Event == "source_event_dropped");
        Assert.Contains(entries, entry =>
            entry.Event == "candidate_rejected"
            && entry.Message.Contains("reason=suppressed_content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunnerSkipsCandidateLogsWhenInfoIsEnabled()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([
            TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete", processId: 42, topLevelWindowName: "Teams notification"),
            TestHelpers.CreateTeamsRecord("Thumbnail Preview", processId: 42, topLevelWindowName: "Teams notification")
        ]);
        var target = new FakeTargetAdapter();
        var runner = new RelayRunner(
            environment,
            CreateConfig("info"),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1"]);

        var logsDir = Path.Combine(environment.RootDirectory, "runtime", "logs");
        await RunUntilAsync(
            runner,
            async () => (await ReadLogEntriesIfExistsAsync(FindInstanceLogPath(logsDir)))
                .Any(entry => entry.Event == "forwarded"));

        var entries = await ReadLogEntriesAsync(FindInstanceLogPath(logsDir));
        Assert.DoesNotContain(entries, entry => entry.Event == "candidate_accepted");
        Assert.DoesNotContain(entries, entry => entry.Event == "candidate_merged");
        Assert.DoesNotContain(entries, entry => entry.Event == "candidate_rejected");
    }

    private static async Task<IReadOnlyList<RelayLogEntry>> ReadLogEntriesAsync(string logPath)
    {
        using var stream = new FileStream(
            logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var lines = content
            .Split(Environment.NewLine, StringSplitOptions.None);
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<RelayLogEntry>(line, LogJsonOptions) ?? throw new InvalidOperationException("Failed to parse relay log entry."))
            .ToArray();
    }

    private static async Task<IReadOnlyList<RelayLogEntry>> ReadLogEntriesIfExistsAsync(string? logPath)
    {
        return string.IsNullOrEmpty(logPath) || !File.Exists(logPath)
            ? []
            : await ReadLogEntriesAsync(logPath);
    }

    private static string FindInstanceLogPath(string logsDirectory)
    {
        return JsonLogWriter.FindLatestLogPath(logsDirectory)
            ?? throw new InvalidOperationException($"No relay log file found in: {logsDirectory}");
    }

    private static async Task RunUntilAsync(
        RelayRunner runner,
        Func<Task<bool>> condition,
        TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource();
        var runTask = runner.RunAsync(cts.Token);
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(5));

        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    if (await condition())
                    {
                        return;
                    }
                }
                catch (IOException)
                {}
                catch (JsonException)
                {}

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            throw new TimeoutException("Timed out waiting for RelayRunner test condition.");
        }
        finally
        {
            cts.Cancel();
            await runTask;
        }
    }

    private static RelayConfig CreateConfig(string logLevel)
    {
        return RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Runtime = new RuntimeOptions
            {
                LogLevel = logLevel
            }
        });
    }

    private sealed class FakeSourceAdapter : IRelaySourceAdapter
    {
        private readonly Queue<RelaySourceRecord> records;
        private readonly Queue<RelaySourceDiagnostic> diagnostics;

        public FakeSourceAdapter(
            IEnumerable<RelaySourceRecord> records,
            IEnumerable<RelaySourceDiagnostic>? diagnostics = null)
        {
            this.records = new Queue<RelaySourceRecord>(records);
            this.diagnostics = new Queue<RelaySourceDiagnostic>(diagnostics ?? []);
        }

        public long DroppedCount => 0;

        public void Dispose()
        {
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public bool TryDequeue(out RelaySourceRecord? record)
        {
            if (records.Count == 0)
            {
                record = null;
                return false;
            }

            record = records.Dequeue();
            return true;
        }

        public bool TryDequeueDiagnostic(out RelaySourceDiagnostic? diagnostic)
        {
            if (diagnostics.Count == 0)
            {
                diagnostic = null;
                return false;
            }

            diagnostic = diagnostics.Dequeue();
            return true;
        }
    }

}

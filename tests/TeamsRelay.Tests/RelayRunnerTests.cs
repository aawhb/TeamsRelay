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

    [Fact]
    public async Task RunnerWritesMemorySnapshotsWhenConfigured()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([])
        {
            RuntimeSnapshot = new RelaySourceRuntimeSnapshot
            {
                QueueDepth = 3,
                DiagnosticQueueDepth = 4,
                PendingCandidateCount = 5,
                DroppedCount = 6,
                WindowOpenedSeen = 10,
                StructureChangedSeen = 20,
                TextExtractionAttempts = 30,
                TextExtractionFailures = 1,
                CandidatesEmitted = 2,
                RejectedBroadRect = 3,
                RejectedProcessNotFound = 4,
                RejectedNotTeamsProcess = 5,
                RejectedNotTeamsWindow = 6,
                RejectedClassifier = 7,
                RejectedSuppressedContent = 8,
                RejectedNotMessageLike = 9
            }
        };
        var target = new FakeTargetAdapter();
        var runner = new RelayRunner(
            environment,
            CreateConfig("info", memorySnapshotIntervalSeconds: 1),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1"]);

        var logsDir = Path.Combine(environment.RootDirectory, "runtime", "logs");
        await RunUntilAsync(
            runner,
            async () => (await ReadLogEntriesIfExistsAsync(FindInstanceLogPath(logsDir)))
                .Any(entry => entry.Event == "memory_snapshot"),
            timeout: TimeSpan.FromSeconds(3));

        var snapshotEntry = (await ReadLogEntriesAsync(FindInstanceLogPath(logsDir)))
            .Single(entry => entry.Event == "memory_snapshot");

        Assert.Contains("queueDepth=3", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("diagnosticQueueDepth=4", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("pendingCandidates=5", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("dropped=6", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("windowOpenedSeen=10", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("structureChangedSeen=20", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("textExtractionAttempts=30", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("textExtractionFailures=1", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("candidatesEmitted=2", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedBroadRect=3", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedProcessNotFound=4", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedNotTeamsProcess=5", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedNotTeamsWindow=6", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedClassifier=7", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedSuppressedContent=8", snapshotEntry.Message, StringComparison.Ordinal);
        Assert.Contains("rejectedNotMessageLike=9", snapshotEntry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunnerSkipsMemorySnapshotsWhenDisabled()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([
            TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete", processId: 42, topLevelWindowName: "Teams notification")
        ]);
        var target = new FakeTargetAdapter();
        var runner = new RelayRunner(
            environment,
            CreateConfig("info", memorySnapshotIntervalSeconds: 0),
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
        Assert.DoesNotContain(entries, entry => entry.Event == "memory_snapshot");
    }

    [Fact]
    public async Task RunnerLogsSourceCountersAsIntervalDeltas()
    {
        var environment = new AppEnvironment(TestHelpers.CreateTemporaryDirectory());
        var source = new FakeSourceAdapter([])
        {
            RuntimeSnapshots = new Queue<RelaySourceRuntimeSnapshot>([
                new RelaySourceRuntimeSnapshot
                {
                    WindowOpenedSeen = 3,
                    StructureChangedSeen = 8,
                    TextExtractionAttempts = 13,
                    TextExtractionFailures = 1,
                    CandidatesEmitted = 2,
                    RejectedBroadRect = 1,
                    RejectedProcessNotFound = 2,
                    RejectedNotTeamsProcess = 3,
                    RejectedNotTeamsWindow = 4,
                    RejectedClassifier = 5,
                    RejectedSuppressedContent = 6,
                    RejectedNotMessageLike = 7
                },
                new RelaySourceRuntimeSnapshot
                {
                    WindowOpenedSeen = 5,
                    StructureChangedSeen = 11,
                    TextExtractionAttempts = 17,
                    TextExtractionFailures = 1,
                    CandidatesEmitted = 3,
                    RejectedBroadRect = 1,
                    RejectedProcessNotFound = 4,
                    RejectedNotTeamsProcess = 3,
                    RejectedNotTeamsWindow = 5,
                    RejectedClassifier = 8,
                    RejectedSuppressedContent = 7,
                    RejectedNotMessageLike = 9
                }
            ])
        };
        var target = new FakeTargetAdapter();
        var runner = new RelayRunner(
            environment,
            CreateConfig("info", memorySnapshotIntervalSeconds: 1),
            source,
            target,
            new FakeProcessNameResolver(),
            ["device-1"]);

        var logsDir = Path.Combine(environment.RootDirectory, "runtime", "logs");
        await RunUntilAsync(
            runner,
            async () => (await ReadLogEntriesIfExistsAsync(FindInstanceLogPath(logsDir)))
                .Count(entry => entry.Event == "memory_snapshot") >= 2,
            timeout: TimeSpan.FromSeconds(4));

        var snapshotEntries = (await ReadLogEntriesAsync(FindInstanceLogPath(logsDir)))
            .Where(entry => entry.Event == "memory_snapshot")
            .ToArray();

        Assert.Contains("windowOpenedSeen=3", snapshotEntries[0].Message, StringComparison.Ordinal);
        Assert.Contains("structureChangedSeen=8", snapshotEntries[0].Message, StringComparison.Ordinal);

        Assert.Contains("windowOpenedSeen=2", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("structureChangedSeen=3", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("textExtractionAttempts=4", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("textExtractionFailures=0", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("candidatesEmitted=1", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedBroadRect=0", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedProcessNotFound=2", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedNotTeamsProcess=0", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedNotTeamsWindow=1", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedClassifier=3", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedSuppressedContent=1", snapshotEntries[1].Message, StringComparison.Ordinal);
        Assert.Contains("rejectedNotMessageLike=2", snapshotEntries[1].Message, StringComparison.Ordinal);
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

    private static RelayConfig CreateConfig(
        string logLevel,
        int memorySnapshotIntervalSeconds = 0,
        string uiaSubscriptionMode = "both")
    {
        return RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Runtime = new RuntimeOptions
            {
                LogLevel = logLevel,
                MemorySnapshotIntervalSeconds = memorySnapshotIntervalSeconds,
                UiaSubscriptionMode = uiaSubscriptionMode
            }
        });
    }

    private sealed class FakeSourceAdapter : IRelaySourceAdapter, IRelaySourceRuntimeDiagnostics
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

        public RelaySourceRuntimeSnapshot RuntimeSnapshot { get; set; } = new();

        public Queue<RelaySourceRuntimeSnapshot>? RuntimeSnapshots { get; set; }

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

        public RelaySourceRuntimeSnapshot GetRuntimeSnapshot()
        {
            if (RuntimeSnapshots is not null && RuntimeSnapshots.Count > 0)
            {
                RuntimeSnapshot = RuntimeSnapshots.Dequeue();
            }

            return RuntimeSnapshot;
        }
    }

}

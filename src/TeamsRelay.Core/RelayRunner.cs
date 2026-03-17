namespace TeamsRelay.Core;

public sealed class RelayRunner
{
    private const int CandidateDebugTextLimit = 160;

    private readonly RelayConfig config;
    private readonly IReadOnlyList<string> deviceIds;
    private readonly JsonLogWriter logWriter;
    private readonly RelayPipeline pipeline;
    private readonly IProcessNameResolver processNameResolver;
    private readonly RelayRuntimePaths runtimePaths;
    private readonly IRelaySourceAdapter sourceAdapter;
    private readonly IRelayTargetAdapter targetAdapter;

    public RelayRunner(
        AppEnvironment environment,
        RelayConfig config,
        IRelaySourceAdapter sourceAdapter,
        IRelayTargetAdapter targetAdapter,
        IProcessNameResolver processNameResolver,
        IReadOnlyList<string> deviceIds)
    {
        this.config = config;
        this.sourceAdapter = sourceAdapter;
        this.targetAdapter = targetAdapter;
        this.deviceIds = deviceIds;
        this.processNameResolver = processNameResolver;
        runtimePaths = RelayRuntimePaths.Create(environment);
        logWriter = new JsonLogWriter(runtimePaths.GenerateInstanceLogPath());
        pipeline = new RelayPipeline(config, processNameResolver);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        runtimePaths.EnsureDirectories();
        sourceAdapter.Start();
        var lastDroppedCount = 0L;

        try
        {
            await logWriter.WriteAsync(new RelayLogEntry
            {
                Event = "startup",
                Message = $"targets={string.Join(",", deviceIds)}"
            }, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(runtimePaths.StopFilePath))
                {
                    await logWriter.WriteAsync(new RelayLogEntry
                    {
                        Event = "stop_signal",
                        Message = runtimePaths.StopFilePath
                    }, cancellationToken);
                    break;
                }

                while (sourceAdapter.TryDequeue(out var record) && record is not null)
                {
                    var addResult = pipeline.Add(record);
                    if (IsDebugLoggingEnabled())
                    {
                        await WriteCandidateDebugLogAsync(record, addResult, cancellationToken);
                    }
                }

                if (IsDebugLoggingEnabled())
                {
                    while (sourceAdapter.TryDequeueDiagnostic(out var diagnostic) && diagnostic is not null)
                    {
                        await WriteSourceDiagnosticLogAsync(diagnostic, cancellationToken);
                    }
                }

                await FlushPipelineAsync(force: false, cancellationToken);
                var currentDropped = sourceAdapter.DroppedCount;
                if (currentDropped > lastDroppedCount)
                {
                    await logWriter.WriteAsync(new RelayLogEntry
                    {
                        Level = "warning",
                        Event = "watcher_drop",
                        Message = $"Dropped {currentDropped - lastDroppedCount} watcher events due to queue pressure."
                    }, cancellationToken);
                    lastDroppedCount = currentDropped;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await FlushPipelineAsync(force: true, shutdownCts.Token);
            }
            catch (Exception)
            {}

            sourceAdapter.Stop();
            if (File.Exists(runtimePaths.StopFilePath))
            {
                File.Delete(runtimePaths.StopFilePath);
            }

            await logWriter.WriteAsync(new RelayLogEntry
            {
                Event = "stopped",
                Message = "Relay stopped."
            }, CancellationToken.None);
        }
    }

    private async Task FlushPipelineAsync(bool force, CancellationToken cancellationToken)
    {
        foreach (var dispatch in pipeline.Flush(force))
        {
            try
            {
                await targetAdapter.SendAsync(config, deviceIds, dispatch.Message, cancellationToken);
                await logWriter.WriteAsync(new RelayLogEntry
                {
                    Event = "forwarded",
                    Message = dispatch.Message
                }, cancellationToken);
            }
            catch (PartialTargetSendException exception)
            {
                await logWriter.WriteAsync(new RelayLogEntry
                {
                    Level = "warning",
                    Event = "target_partial_success",
                    Message = exception.Message
                }, cancellationToken);
            }
            catch (Exception exception)
            {
                await logWriter.WriteAsync(new RelayLogEntry
                {
                    Level = "error",
                    Event = "target_send_failed",
                    Message = exception.Message
                }, cancellationToken);
            }
        }
    }

    private bool IsDebugLoggingEnabled()
    {
        return string.Equals(config.Runtime.LogLevel, "debug", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteCandidateDebugLogAsync(
        RelaySourceRecord record,
        RelayPipelineAddResult addResult,
        CancellationToken cancellationToken)
    {
        var processName = processNameResolver.TryGetProcessName(record.ProcessId) ?? "unknown";
        var textSample = TrimForLog(record.ExtractedText, CandidateDebugTextLimit);
        var message = string.Join(" | ", [
            $"reason={addResult.Reason}",
            $"rawEventKind={ValueOrDash(record.RawEventKind)}",
            $"capturePath={ValueOrDash(record.CapturePath)}",
            $"process={processName}",
            $"processId={record.ProcessId}",
            $"bounds={FormatBounds(record)}",
            $"class={ValueOrDash(record.ClassName)}",
            $"control={ValueOrDash(record.RootControlType)}",
            $"automationId={ValueOrDash(record.AutomationId)}",
            $"topLevelName={ValueOrDash(record.TopLevelWindowName)}",
            $"topLevel={ValueOrDash(record.TopLevelClassName)}",
            $"text={ValueOrDash(textSample)}"
        ]);

        await logWriter.WriteAsync(new RelayLogEntry
        {
            Level = "debug",
            Event = $"candidate_{addResult.Status}",
            Message = message
        }, cancellationToken);
    }

    private async Task WriteSourceDiagnosticLogAsync(
        RelaySourceDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var processName = processNameResolver.TryGetProcessName(diagnostic.ProcessId) ?? "unknown";
        var textSample = TrimForLog(diagnostic.ExtractedText, CandidateDebugTextLimit);
        var message = string.Join(" | ", [
            $"reason={ValueOrDash(diagnostic.Reason)}",
            $"rawEventKind={ValueOrDash(diagnostic.RawEventKind)}",
            $"capturePath={ValueOrDash(diagnostic.CapturePath)}",
            $"process={processName}",
            $"processId={diagnostic.ProcessId}",
            $"bounds={FormatBounds(diagnostic)}",
            $"window={ValueOrDash(diagnostic.WindowName)}",
            $"class={ValueOrDash(diagnostic.ClassName)}",
            $"control={ValueOrDash(diagnostic.RootControlType)}",
            $"automationId={ValueOrDash(diagnostic.AutomationId)}",
            $"topLevelName={ValueOrDash(diagnostic.TopLevelWindowName)}",
            $"topLevelClass={ValueOrDash(diagnostic.TopLevelClassName)}",
            $"text={ValueOrDash(textSample)}"
        ]);

        await logWriter.WriteAsync(new RelayLogEntry
        {
            Level = "debug",
            Event = diagnostic.Event,
            Message = message
        }, cancellationToken);
    }

    private static string FormatBounds(UiElementSnapshot snapshot)
    {
        return snapshot.RectEmpty
            ? "empty"
            : $"{snapshot.Left:0.##},{snapshot.Top:0.##} {snapshot.Width:0.##}x{snapshot.Height:0.##}";
    }

    private static string TrimForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return TextUtilities.TruncateWithEllipsis(normalized, maxLength);
    }

    private static string ValueOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }
}

using TeamsRelay.Core;

namespace TeamsRelay.Lifecycle;

public sealed class RelayRunner
{
    private const int CandidateDebugTextLimit = 160;

    private readonly RelayConfig _config;
    private readonly IReadOnlyList<string> _deviceIds;
    private readonly JsonLogWriter _logWriter;
    private readonly RelayPipeline _pipeline;
    private readonly IProcessNameResolver _processNameResolver;
    private readonly RuntimePaths _runtimePaths;
    private readonly IRelaySourceAdapter _sourceAdapter;
    private readonly IRelayTargetAdapter _targetAdapter;

    public RelayRunner(
        AppEnvironment environment,
        RelayConfig config,
        IRelaySourceAdapter sourceAdapter,
        IRelayTargetAdapter targetAdapter,
        IProcessNameResolver processNameResolver,
        IReadOnlyList<string> deviceIds)
    {
        _config = config;
        _sourceAdapter = sourceAdapter;
        _targetAdapter = targetAdapter;
        _deviceIds = deviceIds;
        _processNameResolver = processNameResolver;
        _runtimePaths = RuntimePaths.Create(environment);
        _logWriter = new JsonLogWriter(_runtimePaths.GenerateInstanceLogPath());
        _pipeline = new RelayPipeline(config, processNameResolver);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _runtimePaths.EnsureDirectories();
        _sourceAdapter.Start();
        var lastDroppedCount = 0L;

        try
        {
            await _logWriter.WriteAsync(new RelayLogEntry
            {
                Event = "startup",
                Message = $"targets={string.Join(",", _deviceIds)}"
            }, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(_runtimePaths.StopFilePath))
                {
                    await _logWriter.WriteAsync(new RelayLogEntry
                    {
                        Event = "stop_signal",
                        Message = _runtimePaths.StopFilePath
                    }, cancellationToken);
                    break;
                }

                while (_sourceAdapter.TryDequeue(out var record) && record is not null)
                {
                    var addResult = _pipeline.Add(record);
                    if (IsDebugLoggingEnabled())
                    {
                        await WriteCandidateDebugLogAsync(record, addResult, cancellationToken);
                    }
                }

                await FlushPipelineAsync(force: false, cancellationToken);
                var currentDropped = _sourceAdapter.DroppedCount;
                if (currentDropped > lastDroppedCount)
                {
                    await _logWriter.WriteAsync(new RelayLogEntry
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
            catch (Exception) { }
            _sourceAdapter.Stop();
            if (File.Exists(_runtimePaths.StopFilePath))
            {
                File.Delete(_runtimePaths.StopFilePath);
            }

            await _logWriter.WriteAsync(new RelayLogEntry
            {
                Event = "stopped",
                Message = "Relay stopped."
            }, CancellationToken.None);

            await _logWriter.DisposeAsync();
        }
    }

    private async Task FlushPipelineAsync(bool force, CancellationToken cancellationToken)
    {
        foreach (var dispatch in _pipeline.Flush(force))
        {
            try
            {
                await _targetAdapter.SendAsync(_config, _deviceIds, dispatch.Message, cancellationToken);
                await _logWriter.WriteAsync(new RelayLogEntry
                {
                    Event = "forwarded",
                    Message = dispatch.Message
                }, cancellationToken);
            }
            catch (PartialTargetSendException exception)
            {
                await _logWriter.WriteAsync(new RelayLogEntry
                {
                    Level = "warning",
                    Event = "target_partial_success",
                    Message = exception.Message
                }, cancellationToken);
            }
            catch (Exception exception)
            {
                await _logWriter.WriteAsync(new RelayLogEntry
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
        return string.Equals(_config.Runtime.LogLevel, "debug", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteCandidateDebugLogAsync(
        RelaySourceRecord record,
        RelayPipelineAddResult addResult,
        CancellationToken cancellationToken)
    {
        var processName = _processNameResolver.TryGetProcessName(record.ProcessId) ?? "unknown";
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

        await _logWriter.WriteAsync(new RelayLogEntry
        {
            Level = "debug",
            Event = $"candidate_{addResult.Status}",
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

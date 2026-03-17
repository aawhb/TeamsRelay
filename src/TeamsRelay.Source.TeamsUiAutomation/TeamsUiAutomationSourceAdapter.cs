using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using TeamsRelay.Core;

namespace TeamsRelay.Source.TeamsUiAutomation;

public sealed class TeamsUiAutomationSourceAdapter : IRelaySourceAdapter
    , IRelaySourceRuntimeDiagnostics
{
    private readonly object gate = new();
    private readonly Queue<RelaySourceRecord> queue = new();
    private readonly Queue<RelaySourceDiagnostic> diagnostics = new();
    private readonly bool enableDiagnostics;
    private readonly int maxQueueSize;
    private readonly int maxDiagnosticQueueSize;
    private readonly TeamsNotificationCaptureClassifier captureClassifier;
    private readonly IProcessNameResolver processNameResolver;
    private readonly RelaySourceRuntimeCounters runtimeCounters = new();
    private readonly string subscriptionMode;
    private Thread? workerThread;
    private Dispatcher? dispatcher;
    private AutomationEventHandler? windowOpenedHandler;
    private StructureChangedEventHandler? structureChangedHandler;
    private long droppedCount;
    private bool started;

    public TeamsUiAutomationSourceAdapter(
        IProcessNameResolver processNameResolver,
        string captureMode = "strict",
        string subscriptionMode = "both",
        int maxQueueSize = 2048,
        bool enableDiagnostics = false,
        int maxDiagnosticQueueSize = 4096)
    {
        this.processNameResolver = processNameResolver;
        captureClassifier = new TeamsNotificationCaptureClassifier(captureMode);
        this.subscriptionMode = subscriptionMode;
        this.maxQueueSize = maxQueueSize;
        this.enableDiagnostics = enableDiagnostics;
        this.maxDiagnosticQueueSize = maxDiagnosticQueueSize;
    }

    public long DroppedCount => Interlocked.Read(ref droppedCount);

    public void Start()
    {
        if (started)
        {
            return;
        }

        Exception? startupException = null;
        using var ready = new ManualResetEventSlim(false);

        workerThread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                windowOpenedHandler = OnWindowOpened;
                structureChangedHandler = OnStructureChanged;

                if (TeamsUiAutomationSubscriptionMode.IncludesWindowOpened(subscriptionMode))
                {
                    Automation.AddAutomationEventHandler(
                        WindowPattern.WindowOpenedEvent,
                        AutomationElement.RootElement,
                        TreeScope.Subtree,
                        windowOpenedHandler);
                }

                if (TeamsUiAutomationSubscriptionMode.IncludesStructureChanged(subscriptionMode))
                {
                    Automation.AddStructureChangedEventHandler(
                        AutomationElement.RootElement,
                        TreeScope.Subtree,
                        structureChangedHandler);
                }
            }
            catch (Exception exception)
            {
                startupException = exception;
                ready.Set();
                return;
            }

            ready.Set();
            Dispatcher.Run();
            CleanupHandlers();
        })
        {
            IsBackground = true,
            Name = "TeamsRelay.UiAutomation"
        };

        workerThread.SetApartmentState(ApartmentState.STA);
        workerThread.Start();
        ready.Wait();

        if (startupException is not null)
        {
            throw startupException;
        }

        started = true;
    }

    public void Stop()
    {
        if (!started)
        {
            return;
        }

        dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);

        if (workerThread?.Join(TimeSpan.FromSeconds(5)) != true)
        {
            try
            {
                Automation.RemoveAllEventHandlers();
            }
            catch (Exception)
            {}
        }

        workerThread = null;
        dispatcher = null;
        started = false;
    }

    public bool TryDequeue(out RelaySourceRecord? record)
    {
        lock (gate)
        {
            FlushExpiredCandidates(DateTimeOffset.UtcNow);
            if (queue.Count == 0)
            {
                record = null;
                return false;
            }

            record = queue.Dequeue();
            return true;
        }
    }

    public bool TryDequeueDiagnostic(out RelaySourceDiagnostic? diagnostic)
    {
        lock (gate)
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

    public void Dispose()
    {
        Stop();
    }

    public RelaySourceRuntimeSnapshot GetRuntimeSnapshot()
    {
        lock (gate)
        {
            return runtimeCounters.CreateSnapshot(
                queue.Count,
                diagnostics.Count,
                captureClassifier.PendingCandidateCount,
                DroppedCount);
        }
    }

    private void OnWindowOpened(object sender, AutomationEventArgs args)
    {
        if (sender is AutomationElement element)
        {
            CaptureEvent(element, "window_opened", applyBroadRectGate: false);
        }
    }

    private void OnStructureChanged(object sender, StructureChangedEventArgs args)
    {
        if (sender is AutomationElement element)
        {
            CaptureEvent(element, "structure_changed", applyBroadRectGate: true);
        }
    }

    private void CaptureEvent(AutomationElement element, string eventKind, bool applyBroadRectGate)
    {
        runtimeCounters.IncrementSeen(eventKind);

        // Fast path: read only ProcessId (single COM call) and filter non-Teams
        // processes before any other COM property reads or parent tree walks.
        int processId;
        try
        {
            processId = element.Current.ProcessId;
        }
        catch (Exception)
        {
            return;
        }

        var processName = processNameResolver.TryGetProcessName(processId);
        if (!IsTeamsRelatedProcess(processName))
        {
            runtimeCounters.IncrementRejection(
                string.IsNullOrWhiteSpace(processName) ? "process_not_found" : "not_teams_process");
            return;
        }

        if (!TryCreateRecord(element, eventKind, processId, out var record))
        {
            return;
        }

        EnqueueDiagnostic("source_event_seen", "raw_capture", record);

        var captureContext = new UiAutomationCaptureContext(
            record.RectEmpty,
            record.Width,
            record.Height,
            record.TopLevelWindowName);
        if (!TeamsUiAutomationCaptureGate.ShouldExtractText(captureContext, processName, applyBroadRectGate, out var rejectionReason))
        {
            runtimeCounters.IncrementRejection(rejectionReason);
            EnqueueDiagnostic("source_event_dropped", rejectionReason, record);
            return;
        }

        runtimeCounters.IncrementTextExtractionAttempt();
        try
        {
            record = record with
            {
                ExtractedText = UiAutomationTextExtractor.ExtractText(element)
            };
        }
        catch (Exception)
        {
            runtimeCounters.IncrementTextExtractionFailure();
            EnqueueDiagnostic("source_event_dropped", "text_extraction_failed", record);
            return;
        }

        lock (gate)
        {
            var candidates = captureClassifier.Process(record);
            if (candidates.Count == 0)
            {
                var diagnosticEvent = DescribeNoCandidateEvent(record);
                var reason = DescribeNoCandidateReason(record);
                if (string.Equals(diagnosticEvent, "source_event_dropped", StringComparison.Ordinal))
                {
                    runtimeCounters.IncrementRejection(reason);
                }

                EnqueueDiagnostic(diagnosticEvent, reason, record);
                return;
            }

            runtimeCounters.IncrementCandidatesEmitted(candidates.Count);
            foreach (var candidate in candidates)
            {
                EnqueueDiagnostic("source_candidate_emitted", candidate.CapturePath, candidate);
                EnqueueCandidate(candidate);
            }
        }
    }

    private static bool IsTeamsRelatedProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return string.Equals(processName, "ms-teams", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "msedgewebview2", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCreateRecord(AutomationElement element, string eventKind, int processId, out RelaySourceRecord record)
    {
        try
        {
            var boundingRectangle = element.Current.BoundingRectangle;
            var topLevelWindow = GetTopLevelWindowInfo(element);
            record = new RelaySourceRecord
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                EventKind = eventKind,
                RawEventKind = eventKind,
                ProcessId = processId,
                WindowName = element.Current.Name ?? string.Empty,
                ClassName = element.Current.ClassName ?? string.Empty,
                RootControlType = element.Current.ControlType.ProgrammaticName ?? string.Empty,
                AutomationId = element.Current.AutomationId ?? string.Empty,
                TopLevelWindowName = topLevelWindow.Name,
                TopLevelClassName = topLevelWindow.ClassName,
                RectEmpty = boundingRectangle.IsEmpty,
                Left = boundingRectangle.Left,
                Top = boundingRectangle.Top,
                Width = boundingRectangle.Width,
                Height = boundingRectangle.Height
            };
            return true;
        }
        catch (Exception)
        {
            record = new RelaySourceRecord();
            return false;
        }
    }

    private void CleanupHandlers()
    {
        try
        {
            if (windowOpenedHandler is not null)
            {
                Automation.RemoveAutomationEventHandler(
                    WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement,
                    windowOpenedHandler);
            }
        }
        catch (Exception)
        {}

        try
        {
            if (structureChangedHandler is not null)
            {
                Automation.RemoveStructureChangedEventHandler(
                    AutomationElement.RootElement,
                    structureChangedHandler);
            }
        }
        catch (Exception)
        { }
        finally
        {
            windowOpenedHandler = null;
            structureChangedHandler = null;
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private void FlushExpiredCandidates(DateTimeOffset nowUtc)
    {
        var expiredCandidates = captureClassifier.FlushExpired(nowUtc);
        runtimeCounters.IncrementCandidatesEmitted(expiredCandidates.Count);

        foreach (var candidate in expiredCandidates)
        {
            EnqueueDiagnostic("source_candidate_emitted", "pending_window_expired", candidate);
            EnqueueCandidate(candidate);
        }
    }

    private void EnqueueCandidate(RelaySourceRecord record)
    {
        if (queue.Count >= maxQueueSize)
        {
            queue.Dequeue();
            Interlocked.Increment(ref droppedCount);
        }

        queue.Enqueue(record);
    }

    private void EnqueueDiagnostic(string eventName, string reason, RelaySourceRecord record)
    {
        if (!enableDiagnostics)
        {
            return;
        }

        if (diagnostics.Count >= maxDiagnosticQueueSize)
        {
            diagnostics.Dequeue();
        }

        diagnostics.Enqueue(RelaySourceDiagnostic.FromRecord(record, eventName, reason));
    }

    private static string DescribeNoCandidateEvent(RelaySourceRecord record)
    {
        return string.Equals(record.RawEventKind, "window_opened", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(record.ExtractedText)
            ? "source_event_pending"
            : "source_event_dropped";
    }

    private static string DescribeNoCandidateReason(RelaySourceRecord record)
    {
        if (string.Equals(record.RawEventKind, "window_opened", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(record.ExtractedText))
        {
            return "awaiting_structure_changed";
        }

        var textAnalysis = TeamsNotificationTextAnalyzer.Analyze(record.ExtractedText);
        if (textAnalysis.ContainsSuppressedContent)
        {
            return "suppressed_content";
        }

        return textAnalysis.LooksLikeRealMessageBanner
            ? "classifier_rejected"
            : "not_message_like";
    }

    private static (string Name, string ClassName) GetTopLevelWindowInfo(AutomationElement element)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? current = element;
            AutomationElement? previous = element;

            while (current is not null)
            {
                previous = current;
                current = walker.GetParent(current);
                if (current is null || current == AutomationElement.RootElement)
                {
                    break;
                }
            }

            return (
                Normalize(previous?.Current.Name),
                Normalize(previous?.Current.ClassName));
        }
        catch (Exception)
        {
            return (string.Empty, string.Empty);
        }
    }
}

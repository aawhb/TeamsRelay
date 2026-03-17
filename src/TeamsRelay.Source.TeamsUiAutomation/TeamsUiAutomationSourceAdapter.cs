using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using TeamsRelay.Core;

namespace TeamsRelay.Source.TeamsUiAutomation;

public sealed class TeamsUiAutomationSourceAdapter : IRelaySourceAdapter
{
    private const int BroadRectMinWidth = 120;
    private const int BroadRectMinHeight = 40;
    private const int BroadRectMaxWidth = 1200;
    private const int BroadRectMaxHeight = 500;

    private readonly object gate = new();
    private readonly Queue<RelaySourceRecord> queue = new();
    private readonly Queue<RelaySourceDiagnostic> diagnostics = new();
    private readonly bool enableDiagnostics;
    private readonly int maxQueueSize;
    private readonly int maxDiagnosticQueueSize;
    private readonly TeamsNotificationCaptureClassifier captureClassifier;
    private Thread? workerThread;
    private Dispatcher? dispatcher;
    private AutomationEventHandler? windowOpenedHandler;
    private StructureChangedEventHandler? structureChangedHandler;
    private long droppedCount;
    private bool started;

    public TeamsUiAutomationSourceAdapter(
        string captureMode = "strict",
        int maxQueueSize = 2048,
        bool enableDiagnostics = false,
        int maxDiagnosticQueueSize = 4096)
    {
        captureClassifier = new TeamsNotificationCaptureClassifier(captureMode);
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

                Automation.AddAutomationEventHandler(
                    WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement,
                    TreeScope.Subtree,
                    windowOpenedHandler);

                Automation.AddStructureChangedEventHandler(
                    AutomationElement.RootElement,
                    TreeScope.Subtree,
                    structureChangedHandler);
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
        RelaySourceRecord record;
        try
        {
            var boundingRectangle = element.Current.BoundingRectangle;
            var topLevelWindow = GetTopLevelWindowInfo(element);
            record = new RelaySourceRecord
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                EventKind = eventKind,
                RawEventKind = eventKind,
                ProcessId = element.Current.ProcessId,
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
                Height = boundingRectangle.Height,
                ExtractedText = ExtractText(element)
            };
        }
        catch (Exception)
        {
            return;
        }

        EnqueueDiagnostic("source_event_seen", "raw_capture", record);

        if (applyBroadRectGate)
        {
            if (record.RectEmpty || record.Width < BroadRectMinWidth || record.Height < BroadRectMinHeight || record.Width > BroadRectMaxWidth || record.Height > BroadRectMaxHeight)
            {
                EnqueueDiagnostic("source_event_dropped", "broad_rect_gate", record);
                return;
            }
        }

        lock (gate)
        {
            var candidates = captureClassifier.Process(record);
            if (candidates.Count == 0)
            {
                EnqueueDiagnostic(DescribeNoCandidateEvent(record), DescribeNoCandidateReason(record), record);
                return;
            }

            foreach (var candidate in candidates)
            {
                EnqueueDiagnostic("source_candidate_emitted", candidate.CapturePath, candidate);
                EnqueueCandidate(candidate);
            }
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

    private static string ExtractText(AutomationElement root)
    {
        const int maxParts = 12;
        var parts = new List<string>(maxParts);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rootName = Normalize(root.Current.Name);
            if (!string.IsNullOrWhiteSpace(rootName))
            {
                seen.Add(rootName);
                parts.Add(rootName);
            }
        }
        catch (Exception)
        {}

        AutomationElementCollection? collection;
        try
        {
            collection = root.FindAll(TreeScope.Subtree, System.Windows.Automation.Condition.TrueCondition);
        }
        catch (Exception)
        {return string.Join(" | ", parts);
        }

        if (collection is null)
        {
            return string.Join(" | ", parts);
        }

        for (var index = 0; index < collection.Count; index++)
        {
            var candidate = collection[index];
            if (candidate is null)
            {
                continue;
            }

            string controlTypeName;
            try
            {
                controlTypeName = candidate.Current.ControlType.ProgrammaticName ?? string.Empty;
            }
            catch (Exception)
            {
                continue;
            }

            if (!IsAllowedControlType(controlTypeName))
            {
                continue;
            }

            string candidateText;
            try
            {
                candidateText = Normalize(candidate.Current.Name);
            }
            catch (Exception)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidateText) || candidateText.Length < 2 || !seen.Add(candidateText))
            {
                continue;
            }

            parts.Add(candidateText);
            if (parts.Count >= maxParts)
            {
                break;
            }
        }

        return string.Join(" | ", parts);
    }

    private static bool IsAllowedControlType(string programmaticName)
    {
        return programmaticName.EndsWith(".Text", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Document", StringComparison.Ordinal)
            || programmaticName.EndsWith(".ListItem", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Custom", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Pane", StringComparison.Ordinal);
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
        foreach (var candidate in captureClassifier.FlushExpired(nowUtc))
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

using System.Threading;
using System.Windows.Automation;
using System.Windows.Threading;
using TeamsRelay.Core;

namespace TeamsRelay.Source.TeamsUiAutomation;

public sealed class TeamsUiAutomationSourceAdapter : IRelaySourceAdapter
{
    private readonly object _gate = new();
    private readonly Queue<RelaySourceRecord> _queue = new();
    private readonly int _maxQueueSize;
    private readonly TeamsNotificationCaptureClassifier _captureClassifier;
    private readonly IProcessNameResolver _processNameResolver;
    private Thread? _workerThread;
    private Dispatcher? _dispatcher;
    private AutomationEventHandler? _windowOpenedHandler;
    private StructureChangedEventHandler? _structureChangedHandler;
    private long _droppedCount;
    private bool _started;

    public TeamsUiAutomationSourceAdapter(
        IProcessNameResolver processNameResolver,
        int maxQueueSize = 2048)
    {
        _processNameResolver = processNameResolver;
        _captureClassifier = new TeamsNotificationCaptureClassifier();
        _maxQueueSize = maxQueueSize;
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public void Start()
    {
        if (_started)
        {
            return;
        }

        Exception? startupException = null;
        using var ready = new ManualResetEventSlim(false);

        _workerThread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _windowOpenedHandler = OnWindowOpened;
                _structureChangedHandler = OnStructureChanged;

                Automation.AddAutomationEventHandler(
                    WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement,
                    TreeScope.Subtree,
                    _windowOpenedHandler);

                Automation.AddStructureChangedEventHandler(
                    AutomationElement.RootElement,
                    TreeScope.Subtree,
                    _structureChangedHandler);
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

        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();
        ready.Wait();

        if (startupException is not null)
        {
            throw startupException;
        }

        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);

        if (_workerThread?.Join(TimeSpan.FromSeconds(5)) != true)
        {
            try
            {
                Automation.RemoveAllEventHandlers();
            }
            catch (Exception) { }
        }

        _workerThread = null;
        _dispatcher = null;
        _started = false;
    }

    public bool TryDequeue(out RelaySourceRecord? record)
    {
        lock (_gate)
        {
            FlushExpiredCandidates(DateTimeOffset.UtcNow);
            if (_queue.Count == 0)
            {
                record = null;
                return false;
            }

            record = _queue.Dequeue();
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
            CaptureEvent(element, RelayEventKinds.WindowOpened, applyBroadRectGate: false);
        }
    }

    private void OnStructureChanged(object sender, StructureChangedEventArgs args)
    {
        if (sender is AutomationElement element)
        {
            CaptureEvent(element, RelayEventKinds.StructureChanged, applyBroadRectGate: true);
        }
    }

    private void CaptureEvent(AutomationElement element, string eventKind, bool applyBroadRectGate)
    {
        // Filter on ProcessId alone before any other COM reads to skip non-Teams events cheaply.
        int processId;
        try
        {
            processId = element.Current.ProcessId;
        }
        catch (Exception)
        {
            return;
        }

        var processName = _processNameResolver.TryGetProcessName(processId);
        if (!TeamsProcessNames.IsTeamsRelated(processName))
        {
            return;
        }

        if (!TryCreateRecord(element, eventKind, processId, out var record))
        {
            return;
        }

        var captureContext = new UiAutomationCaptureContext(
            record.RectEmpty,
            record.Width,
            record.Height,
            record.TopLevelWindowName);
        if (!TeamsUiAutomationCaptureGate.ShouldExtractText(captureContext, processName, applyBroadRectGate, out _))
        {
            return;
        }

        try
        {
            record = record with
            {
                ExtractedText = UiAutomationTextExtractor.ExtractText(element)
            };
        }
        catch (Exception)
        {
            return;
        }

        lock (_gate)
        {
            var candidates = _captureClassifier.Process(record);
            foreach (var candidate in candidates)
            {
                EnqueueCandidate(candidate);
            }
        }
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
            if (_windowOpenedHandler is not null)
            {
                Automation.RemoveAutomationEventHandler(
                    WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement,
                    _windowOpenedHandler);
            }
        }
        catch (Exception) { }
        try
        {
            if (_structureChangedHandler is not null)
            {
                Automation.RemoveStructureChangedEventHandler(
                    AutomationElement.RootElement,
                    _structureChangedHandler);
            }
        }
        catch (Exception) { }
        finally
        {
            _windowOpenedHandler = null;
            _structureChangedHandler = null;
        }
    }

    private void FlushExpiredCandidates(DateTimeOffset nowUtc)
    {
        var expiredCandidates = _captureClassifier.FlushExpired(nowUtc);
        foreach (var candidate in expiredCandidates)
        {
            EnqueueCandidate(candidate);
        }
    }

    private void EnqueueCandidate(RelaySourceRecord record)
    {
        if (_queue.Count >= _maxQueueSize)
        {
            _queue.Dequeue();
            Interlocked.Increment(ref _droppedCount);
        }

        _queue.Enqueue(record);
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
                TextUtilities.NormalizeWhitespace(previous?.Current.Name),
                TextUtilities.NormalizeWhitespace(previous?.Current.ClassName));
        }
        catch (Exception)
        {
            return (string.Empty, string.Empty);
        }
    }
}

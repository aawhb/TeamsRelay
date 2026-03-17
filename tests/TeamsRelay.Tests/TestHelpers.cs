using System.Text;
using TeamsRelay.Core;

namespace TeamsRelay.Tests;

internal static class TestHelpers
{
    public static (StringWriter Stdout, StringWriter Stderr) CreateWriters()
    {
        return (new StringWriter(new StringBuilder()), new StringWriter(new StringBuilder()));
    }

    public static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "teamsrelay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static RelaySourceRecord CreateTeamsRecord(
        string extractedText,
        string eventKind = "window_opened",
        string capturePath = "window_opened",
        DateTimeOffset? timestampUtc = null,
        int processId = 4242,
        string windowName = "Microsoft Teams",
        string className = "Chrome_WidgetWin_1",
        string rootControlType = "ControlType.Pane",
        string automationId = "toast-root",
        string topLevelWindowName = "Microsoft Teams notification",
        string topLevelClassName = "Chrome_WidgetWin_1",
        bool rectEmpty = false,
        double left = 100,
        double top = 100,
        double width = 320,
        double height = 120)
    {
        return new RelaySourceRecord
        {
            TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow,
            EventKind = eventKind,
            RawEventKind = eventKind,
            CapturePath = capturePath,
            ProcessId = processId,
            WindowName = windowName,
            ClassName = className,
            RootControlType = rootControlType,
            AutomationId = automationId,
            TopLevelWindowName = topLevelWindowName,
            TopLevelClassName = topLevelClassName,
            RectEmpty = rectEmpty,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ExtractedText = extractedText
        };
    }

    public static RelaySourceRecord CreateRecord(
        DateTimeOffset timestampUtc,
        string rawEventKind,
        string extractedText,
        string windowName = "Microsoft Teams",
        string automationId = "toast-root",
        string topLevelWindowName = "Microsoft Teams notification",
        double left = 100,
        double top = 100,
        double width = 320,
        double height = 120)
    {
        return CreateTeamsRecord(
            extractedText: extractedText,
            eventKind: rawEventKind,
            timestampUtc: timestampUtc,
            windowName: windowName,
            automationId: automationId,
            topLevelWindowName: topLevelWindowName,
            left: left,
            top: top,
            width: width,
            height: height);
    }

    public static RelaySourceDiagnostic CreateDiagnostic(
        string eventName,
        string reason,
        string extractedText = "Message preview | Alex | Build complete",
        string rawEventKind = "structure_changed",
        string capturePath = "structure_changed_banner",
        DateTimeOffset? timestampUtc = null,
        int processId = 42,
        string windowName = "Microsoft Teams",
        string className = "Chrome_WidgetWin_1",
        string rootControlType = "ControlType.Pane",
        string automationId = "toast-root",
        string topLevelWindowName = "Teams notification",
        string topLevelClassName = "Chrome_WidgetWin_1",
        bool rectEmpty = false,
        double left = 100,
        double top = 100,
        double width = 320,
        double height = 120)
    {
        return new RelaySourceDiagnostic
        {
            TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow,
            Event = eventName,
            Reason = reason,
            RawEventKind = rawEventKind,
            CapturePath = capturePath,
            ProcessId = processId,
            WindowName = windowName,
            ClassName = className,
            RootControlType = rootControlType,
            AutomationId = automationId,
            TopLevelWindowName = topLevelWindowName,
            TopLevelClassName = topLevelClassName,
            RectEmpty = rectEmpty,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ExtractedText = extractedText
        };
    }
}

internal sealed class FakeProcessNameResolver : IProcessNameResolver
{
    private readonly string processName;

    public FakeProcessNameResolver(string processName = "ms-teams")
    {
        this.processName = processName;
    }

    public string? TryGetProcessName(int processId)
    {
        return processName;
    }
}

internal sealed class FakeTargetAdapter : IRelayTargetAdapter
{
    public List<string> Messages { get; } = [];

    public bool ThrowOnSend { get; set; }

    public PartialTargetSendException? PartialFailure { get; set; }

    public string Kind => "fake";

    public Task<IReadOnlyList<RelayDevice>> GetDeviceInventoryAsync(RelayConfig config, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RelayDevice>>([]);

    public Task<RelayDiagnosticReport> RunDoctorAsync(RelayConfig config, CancellationToken cancellationToken = default)
        => Task.FromResult(new RelayDiagnosticReport());

    public Task SendAsync(RelayConfig config, IReadOnlyList<string> deviceIds, string message, CancellationToken cancellationToken = default)
    {
        if (PartialFailure is not null)
        {
            throw PartialFailure;
        }

        if (ThrowOnSend)
        {
            throw new InvalidOperationException("simulated send failure");
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}

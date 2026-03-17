namespace TeamsRelay.Core;

public sealed record RelaySourceDiagnostic : UiElementSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Event { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public string RawEventKind { get; init; } = string.Empty;

    public string CapturePath { get; init; } = string.Empty;

    public static RelaySourceDiagnostic FromRecord(
        RelaySourceRecord record,
        string eventName,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new RelaySourceDiagnostic
        {
            TimestampUtc = record.TimestampUtc,
            Event = eventName,
            Reason = reason,
            RawEventKind = record.RawEventKind,
            CapturePath = record.CapturePath,
            ProcessId = record.ProcessId,
            WindowName = record.WindowName,
            ClassName = record.ClassName,
            RootControlType = record.RootControlType,
            AutomationId = record.AutomationId,
            TopLevelWindowName = record.TopLevelWindowName,
            TopLevelClassName = record.TopLevelClassName,
            RectEmpty = record.RectEmpty,
            Left = record.Left,
            Top = record.Top,
            Width = record.Width,
            Height = record.Height,
            ExtractedText = record.ExtractedText
        };
    }
}

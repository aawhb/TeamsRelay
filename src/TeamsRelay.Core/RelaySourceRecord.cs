namespace TeamsRelay.Core;

public sealed record RelaySourceRecord : UiElementSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string EventKind { get; init; } = string.Empty;

    public string RawEventKind { get; init; } = string.Empty;

    public string CapturePath { get; init; } = string.Empty;
}

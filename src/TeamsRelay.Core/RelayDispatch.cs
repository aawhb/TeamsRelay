namespace TeamsRelay.Core;

public sealed record RelayDispatch
{
    public string Message { get; init; } = string.Empty;

    public string Fingerprint { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public string WindowName { get; init; } = string.Empty;

    public string EventKind { get; init; } = string.Empty;

    public string CapturePath { get; init; } = string.Empty;

    public string MessageExtracted { get; init; } = string.Empty;

    public bool FallbackUsed { get; init; }

    public bool HasContent { get; init; }

    public DateTimeOffset SeenUtc { get; init; } = DateTimeOffset.UtcNow;
}

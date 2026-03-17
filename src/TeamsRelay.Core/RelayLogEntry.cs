namespace TeamsRelay.Core;

public sealed record RelayLogEntry
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Level { get; init; } = "info";

    public string Event { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

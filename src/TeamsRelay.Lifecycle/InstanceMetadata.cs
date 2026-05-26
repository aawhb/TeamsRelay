namespace TeamsRelay.Lifecycle;

public sealed record InstanceMetadata
{
    public int ProcessId { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public string ConfigPath { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string[] DeviceNames { get; init; } = [];
}

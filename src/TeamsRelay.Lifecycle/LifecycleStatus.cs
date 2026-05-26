namespace TeamsRelay.Lifecycle;

public sealed record LifecycleStatus
{
    public LifecycleState ProcessState { get; init; }

    public int? ProcessId { get; init; }

    public InstanceMetadata? Metadata { get; init; }
}

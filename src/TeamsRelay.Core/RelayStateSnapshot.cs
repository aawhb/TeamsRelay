namespace TeamsRelay.Core;

public sealed record RelayStateSnapshot
{
    public RelayProcessState ProcessState { get; init; }

    public int? ProcessId { get; init; }

    public RelayInstanceMetadata? Metadata { get; init; }
}

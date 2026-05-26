namespace TeamsRelay.Core;

public sealed record RelayDevice
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsAvailable { get; init; }
}

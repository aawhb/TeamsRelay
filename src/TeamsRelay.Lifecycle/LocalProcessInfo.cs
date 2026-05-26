namespace TeamsRelay.Lifecycle;

public readonly record struct LocalProcessInfo(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    string? CommandLine);

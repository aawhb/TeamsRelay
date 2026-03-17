namespace TeamsRelay.Core;

public sealed record RelayDiagnosticCheck
{
    public string Name { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public string Details { get; init; } = string.Empty;

    public bool IsBlocking { get; init; } = true;
}

namespace TeamsRelay.Core;

public sealed record RelayDiagnosticReport
{
    public IReadOnlyList<RelayDiagnosticCheck> Checks { get; init; } = [];

    public bool HasBlockingFailures => Checks.Any(check => check.IsBlocking && !check.Passed);
}

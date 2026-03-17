namespace TeamsRelay.Target.KdeConnect;

public sealed record KdeCommandResult
{
    public int ExitCode { get; init; }

    public IReadOnlyList<string> OutputLines { get; init; } = [];
}

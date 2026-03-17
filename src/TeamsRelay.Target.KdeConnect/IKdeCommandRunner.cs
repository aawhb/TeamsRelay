namespace TeamsRelay.Target.KdeConnect;

public interface IKdeCommandRunner
{
    Task<KdeCommandResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

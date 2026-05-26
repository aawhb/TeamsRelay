namespace TeamsRelay.Core;

public sealed class CliException : Exception
{
    public CliException(string message, int exitCode = 1)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

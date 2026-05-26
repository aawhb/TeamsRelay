using System.Diagnostics;

namespace TeamsRelay.Target.KdeConnect;

public sealed class ProcessKdeCommandRunner : IKdeCommandRunner
{
    public async Task<KdeCommandResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        return new KdeCommandResult
        {
            ExitCode = process.ExitCode,
            OutputLines = SplitLines(standardOutput, standardError)
        };
    }

    private static IReadOnlyList<string> SplitLines(params string[] sources)
    {
        return sources
            .SelectMany(source => source.Split(["\r\n", "\n"], StringSplitOptions.None))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
}

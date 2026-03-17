using System.Diagnostics;

namespace TeamsRelay.App;

public sealed class SelfProcessInvoker
{
    private readonly string executablePath;
    private readonly string[] bootstrapArguments;

    private SelfProcessInvoker(string executablePath, string[] bootstrapArguments)
    {
        this.executablePath = executablePath;
        this.bootstrapArguments = bootstrapArguments;
    }

    public static SelfProcessInvoker Detect()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");
        var entryPointPath = Environment.GetCommandLineArgs()[0];

        if (Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return new SelfProcessInvoker(processPath, [entryPointPath]);
        }

        return new SelfProcessInvoker(processPath, []);
    }

    public ProcessStartInfo CreateBackgroundRunStartInfo(string workingDirectory, string configPath, IReadOnlyList<string> deviceIds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in bootstrapArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--background");
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);

        foreach (var deviceId in deviceIds)
        {
            startInfo.ArgumentList.Add("--device-id");
            startInfo.ArgumentList.Add(deviceId);
        }

        startInfo.Environment["TEAMSRELAY_ROOT"] = workingDirectory;
        return startInfo;
    }
}

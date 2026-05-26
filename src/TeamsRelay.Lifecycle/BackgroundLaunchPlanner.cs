using System.Diagnostics;

namespace TeamsRelay.Lifecycle;

public static class BackgroundLaunchPlanner
{
    public static (string ExecutablePath, IReadOnlyList<string> BootstrapArguments) DetectHost()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Current process path is unavailable.");
        var entryPointPath = Environment.GetCommandLineArgs()[0];

        if (Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, new[] { entryPointPath });
        }

        return (processPath, Array.Empty<string>());
    }

    public static ProcessStartInfo Plan(
        string executablePath,
        IReadOnlyList<string> bootstrapArguments,
        string workingDirectory,
        string configPath,
        IReadOnlyList<string> deviceIds)
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

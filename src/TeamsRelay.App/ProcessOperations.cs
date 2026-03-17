using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace TeamsRelay.App;

internal readonly record struct LocalProcessInfo(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    string? CommandLine);

internal interface IProcessOperations
{
    int CurrentProcessId { get; }

    bool IsRunning(int processId);

    void Kill(int processId);

    IReadOnlyList<LocalProcessInfo> FindRepoLocalTeamsRelayProcesses(string rootDirectory);
}

internal sealed class RuntimeProcessOperations : IProcessOperations
{
    private static readonly Regex CommandLineTokenPattern = new(
        "\"(?<token>[^\"]+)\"|(?<token>\\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int CurrentProcessId => Environment.ProcessId;

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception)
        {}
    }

    public IReadOnlyList<LocalProcessInfo> FindRepoLocalTeamsRelayProcesses(string rootDirectory)
    {
        var processes = new List<LocalProcessInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'TeamsRelay.exe' OR Name = 'dotnet.exe'");
            using var results = searcher.Get();

            foreach (ManagementObject process in results)
            {
                var processId = Convert.ToInt32(process["ProcessId"]);
                var info = new LocalProcessInfo(
                    processId,
                    process["Name"]?.ToString() ?? string.Empty,
                    process["ExecutablePath"]?.ToString(),
                    process["CommandLine"]?.ToString());

                if (IsRepoLocalTeamsRelayProcess(info, rootDirectory))
                {
                    processes.Add(info);
                }
            }
        }
        catch (Exception)
        {
            return [];
        }

        return processes;
    }

    internal static bool IsRepoLocalTeamsRelayProcess(LocalProcessInfo process, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return false;
        }

        if (Path.GetFileName(process.Name).Equals("TeamsRelay.exe", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(process.ExecutablePath), "TeamsRelay.exe", StringComparison.OrdinalIgnoreCase)
            && IsPathUnderRoot(process.ExecutablePath, rootDirectory))
        {
            return true;
        }

        if (!Path.GetFileName(process.Name).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var managedEntryPoint = ExtractManagedEntryPoint(process.CommandLine);
        return !string.IsNullOrWhiteSpace(managedEntryPoint)
            && Path.GetFileName(managedEntryPoint).Equals("TeamsRelay.dll", StringComparison.OrdinalIgnoreCase)
            && IsPathUnderRoot(managedEntryPoint, rootDirectory);
    }

    private static string? ExtractManagedEntryPoint(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        foreach (Match match in CommandLineTokenPattern.Matches(commandLine))
        {
            var token = match.Groups["token"].Value;
            if (token.EndsWith("TeamsRelay.dll", StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }
        }

        return null;
    }

    private static bool IsPathUnderRoot(string? candidatePath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var normalizedRoot = NormalizePath(rootDirectory);
            var normalizedCandidate = NormalizePath(candidatePath);
            return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

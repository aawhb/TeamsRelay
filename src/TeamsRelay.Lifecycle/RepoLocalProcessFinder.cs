using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;

namespace TeamsRelay.Lifecycle;

public sealed class RepoLocalProcessFinder
{
    private static readonly Regex CommandLineTokenPattern = new(
        "\"(?<token>[^\"]+)\"|(?<token>\\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<LocalProcessInfo> Find(string rootDirectory)
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
        catch (ManagementException)
        {
            return [];
        }
        catch (COMException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return processes;
    }

    public static bool IsRepoLocalTeamsRelayProcess(LocalProcessInfo process, string rootDirectory)
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
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
        catch (SecurityException)
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

using System.Diagnostics;
using System.ComponentModel;

namespace TeamsRelay.Lifecycle;

public sealed class SystemProcessLauncher : IProcessLauncher
{
    public static readonly SystemProcessLauncher Instance = new();

    public int CurrentProcessId => Environment.ProcessId;

    public IProcessHandle Spawn(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        return new SystemProcessHandle(process);
    }

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
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
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    public bool TryGetStartTime(int processId, out DateTimeOffset startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            startTimeUtc = new DateTimeOffset(process.StartTime);
            return true;
        }
        catch (ArgumentException)
        {
            startTimeUtc = default;
            return false;
        }
        catch (InvalidOperationException)
        {
            startTimeUtc = default;
            return false;
        }
        catch (Win32Exception)
        {
            startTimeUtc = default;
            return false;
        }
    }

    private sealed class SystemProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public SystemProcessHandle(Process process)
        {
            _process = process;
        }

        public int Id => _process.Id;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public void Refresh() => _process.Refresh();

        public void Dispose() => _process.Dispose();
    }
}

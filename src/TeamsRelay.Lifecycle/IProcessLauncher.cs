using System.Diagnostics;

namespace TeamsRelay.Lifecycle;

public interface IProcessLauncher
{
    int CurrentProcessId { get; }

    IProcessHandle Spawn(ProcessStartInfo startInfo);

    bool IsRunning(int processId);

    void Kill(int processId);

    bool TryGetStartTime(int processId, out DateTimeOffset startTimeUtc);
}

using System.Diagnostics;
using TeamsRelay.Lifecycle;

namespace TeamsRelay.App;

internal interface IProcessOperations
{
    int CurrentProcessId { get; }

    IProcessHandle Spawn(ProcessStartInfo startInfo);

    bool IsRunning(int processId);

    void Kill(int processId);

    IReadOnlyList<LocalProcessInfo> FindRepoLocalTeamsRelayProcesses(string rootDirectory);
}

internal sealed class RuntimeProcessOperations : IProcessOperations
{
    private readonly IProcessLauncher _launcher;
    private readonly RepoLocalProcessFinder _finder;

    public RuntimeProcessOperations()
        : this(SystemProcessLauncher.Instance, new RepoLocalProcessFinder())
    {
    }

    public RuntimeProcessOperations(IProcessLauncher launcher, RepoLocalProcessFinder finder)
    {
        _launcher = launcher;
        _finder = finder;
    }

    public int CurrentProcessId => _launcher.CurrentProcessId;

    public IProcessHandle Spawn(ProcessStartInfo startInfo) => _launcher.Spawn(startInfo);

    public bool IsRunning(int processId) => _launcher.IsRunning(processId);

    public void Kill(int processId) => _launcher.Kill(processId);

    public IReadOnlyList<LocalProcessInfo> FindRepoLocalTeamsRelayProcesses(string rootDirectory)
        => _finder.Find(rootDirectory);
}

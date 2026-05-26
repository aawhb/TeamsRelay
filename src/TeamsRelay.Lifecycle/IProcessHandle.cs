namespace TeamsRelay.Lifecycle;

public interface IProcessHandle : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int ExitCode { get; }

    void Refresh();
}

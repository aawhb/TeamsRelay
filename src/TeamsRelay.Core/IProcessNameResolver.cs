namespace TeamsRelay.Core;

public interface IProcessNameResolver
{
    string? TryGetProcessName(int processId);
}

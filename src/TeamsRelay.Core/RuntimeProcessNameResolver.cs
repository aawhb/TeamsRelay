using System.Diagnostics;

namespace TeamsRelay.Core;

public sealed class RuntimeProcessNameResolver : IProcessNameResolver
{
    public string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

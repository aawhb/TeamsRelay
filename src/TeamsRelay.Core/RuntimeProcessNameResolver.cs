using System.Collections.Concurrent;
using System.Diagnostics;

namespace TeamsRelay.Core;

public sealed class RuntimeProcessNameResolver : IProcessNameResolver
{
    private readonly ConcurrentDictionary<int, CachedEntry> cache = new();
    private const long CacheLifetimeMilliseconds = 2000;

    public string? TryGetProcessName(int processId)
    {
        var nowMs = Environment.TickCount64;

        if (cache.TryGetValue(processId, out var entry)
            && (nowMs - entry.RefreshedAtMs) < CacheLifetimeMilliseconds)
        {
            return entry.Name;
        }

        string? name;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception)
        {
            name = null;
        }

        cache[processId] = new CachedEntry(name, nowMs);
        return name;
    }

    private readonly record struct CachedEntry(string? Name, long RefreshedAtMs);
}

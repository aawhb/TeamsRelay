using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace TeamsRelay.Core;

public sealed class RuntimeProcessNameResolver : IProcessNameResolver
{
    private const long CacheLifetimeMilliseconds = 2000;
    private const long EvictionAgeMilliseconds = 30000;
    private const int EvictionThreshold = 256;

    private readonly ConcurrentDictionary<int, CachedEntry> _cache = new();

    public string? TryGetProcessName(int processId)
    {
        var nowMs = Environment.TickCount64;

        if (_cache.TryGetValue(processId, out var entry)
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
        catch (ArgumentException)
        {
            name = null;
        }
        catch (InvalidOperationException)
        {
            name = null;
        }
        catch (Win32Exception)
        {
            name = null;
        }
        catch (NotSupportedException)
        {
            name = null;
        }

        _cache[processId] = new CachedEntry(name, nowMs);

        if (_cache.Count > EvictionThreshold)
        {
            EvictStale(nowMs);
        }

        return name;
    }

    private void EvictStale(long nowMs)
    {
        foreach (var pair in _cache)
        {
            if ((nowMs - pair.Value.RefreshedAtMs) > EvictionAgeMilliseconds)
            {
                _cache.TryRemove(pair.Key, out _);
            }
        }
    }

    private readonly record struct CachedEntry(string? Name, long RefreshedAtMs);
}

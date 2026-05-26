using System.Text.Json;

namespace TeamsRelay.Core;

public sealed class JsonLogWriter : IAsyncDisposable
{
    private readonly string _logPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private StreamWriter? _writer;
    private bool _disposed;

    public JsonLogWriter(string logPath)
    {
        _logPath = logPath;
    }

    public string LogPath => _logPath;

    public async Task WriteAsync(RelayLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var line = JsonSerializer.Serialize(entry, JsonDefaults.Compact);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return;
            }

            _writer ??= OpenWriter(_logPath);
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public IReadOnlyList<string> ReadTail(int lineCount = 120)
    {
        if (!File.Exists(_logPath))
        {
            return [];
        }

        return File.ReadLines(_logPath)
            .TakeLast(lineCount)
            .ToArray();
    }

    public static string? FindLatestLogPath(string logsDirectory)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return null;
        }

        return Directory.GetFiles(logsDirectory, "relay-*.log")
            .OrderDescending()
            .FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_writer is not null)
            {
                await _writer.FlushAsync();
                await _writer.DisposeAsync();
                _writer = null;
            }
        }
        finally
        {
            _writeGate.Release();
            _writeGate.Dispose();
        }
    }

    private static StreamWriter OpenWriter(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        return new StreamWriter(stream) { AutoFlush = false };
    }
}

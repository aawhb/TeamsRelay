using System.Text.Json;

namespace TeamsRelay.Core;

public sealed class JsonLogWriter
{
    private readonly string logPath;

    public JsonLogWriter(string logPath)
    {
        this.logPath = logPath;
    }

    public string LogPath => logPath;

    public async Task WriteAsync(RelayLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(entry, JsonDefaults.Compact) + Environment.NewLine;
        await File.AppendAllTextAsync(logPath, line, cancellationToken);
    }

    public IReadOnlyList<string> ReadTail(int lineCount = 120)
    {
        if (!File.Exists(logPath))
        {
            return [];
        }

        return File.ReadLines(logPath)
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
}

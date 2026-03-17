namespace TeamsRelay.Core;

public sealed record RelayRuntimePaths(
    string RuntimeDirectory,
    string StateDirectory,
    string LogsDirectory,
    string PidFilePath,
    string MetadataFilePath,
    string StopFilePath)
{
    public static RelayRuntimePaths Create(AppEnvironment environment)
    {
        var runtimeDirectory = environment.ResolvePath("runtime");
        var stateDirectory = Path.Combine(runtimeDirectory, "state");
        var logsDirectory = Path.Combine(runtimeDirectory, "logs");

        return new RelayRuntimePaths(
            runtimeDirectory,
            stateDirectory,
            logsDirectory,
            Path.Combine(stateDirectory, "relay.pid"),
            Path.Combine(stateDirectory, "relay.meta.json"),
            Path.Combine(stateDirectory, "relay.stop"));
    }

    public string GenerateInstanceLogPath()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(LogsDirectory, $"relay-{timestamp}.log");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RuntimeDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

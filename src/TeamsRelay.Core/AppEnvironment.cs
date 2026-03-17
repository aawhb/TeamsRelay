namespace TeamsRelay.Core;

public sealed class AppEnvironment
{
    public AppEnvironment(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; init; }

    public string DefaultConfigPath => ResolvePath(Path.Combine("config", "relay.config.json"));

    public static AppEnvironment Detect()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("TEAMSRELAY_ROOT");
        var rootDirectory = string.IsNullOrWhiteSpace(configuredRoot)
            ? Directory.GetCurrentDirectory()
            : configuredRoot;

        return new AppEnvironment(rootDirectory);
    }

    public string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(RootDirectory, path));
    }
}

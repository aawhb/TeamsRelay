using System.Text.Json;

namespace TeamsRelay.Core;

public sealed class ConfigFileService
{
    private readonly AppEnvironment environment;

    public ConfigFileService(AppEnvironment environment)
    {
        this.environment = environment;
    }

    public async Task<string> InitializeAsync(string? path, bool force, CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveConfigPath(path);
        var parentDirectory = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new CliException($"Could not determine config directory for '{resolvedPath}'.");
        }

        Directory.CreateDirectory(parentDirectory);

        if (File.Exists(resolvedPath) && !force)
        {
            throw new CliException($"Config already exists: {resolvedPath} (use --force to overwrite).");
        }

        var content = JsonSerializer.Serialize(RelayConfig.CreateDefault(), JsonDefaults.Parsing) + Environment.NewLine;
        await File.WriteAllTextAsync(resolvedPath, content, cancellationToken);
        return resolvedPath;
    }

    public async Task<ResolvedRelayConfig> LoadAsync(string? path, CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveConfigPath(path);
        if (!File.Exists(resolvedPath))
        {
            throw new CliException($"Config file not found: {resolvedPath}");
        }

        RelayConfig? parsed;
        try
        {
            var rawJson = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
            parsed = JsonSerializer.Deserialize<RelayConfig>(rawJson, JsonDefaults.Parsing);
        }
        catch (JsonException exception)
        {
            throw new CliException($"Failed to parse config '{resolvedPath}': {exception.Message}");
        }

        var validated = RelayConfig.NormalizeAndValidate(parsed);
        return new ResolvedRelayConfig(resolvedPath, validated);
    }

    public string ResolveConfigPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? environment.DefaultConfigPath
            : environment.ResolvePath(path);
    }
}

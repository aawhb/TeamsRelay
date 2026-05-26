using System.Text.Json;

namespace TeamsRelay.Core;

public sealed class ConfigFileService
{
    private readonly AppEnvironment _environment;

    public ConfigFileService(AppEnvironment environment)
    {
        _environment = environment;
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
            ? _environment.DefaultConfigPath
            : _environment.ResolvePath(path);
    }
}

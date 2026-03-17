namespace TeamsRelay.Core;

public static class CommandLocator
{
    public static string Resolve(AppEnvironment environment, string commandOrPath)
    {
        if (string.IsNullOrWhiteSpace(commandOrPath))
        {
            throw new CliException("Command or path cannot be empty.");
        }

        if (commandOrPath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            var resolvedPath = environment.ResolvePath(commandOrPath);
            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            throw new CliException($"Could not resolve command/path: {commandOrPath}");
        }

        if (Path.IsPathRooted(commandOrPath))
        {
            var resolvedPath = Path.GetFullPath(commandOrPath);
            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            throw new CliException($"Could not resolve command/path: {commandOrPath}");
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [".exe", ".cmd", ".bat"];

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in EnumerateCandidates(commandOrPath, pathExtensions))
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        throw new CliException($"Could not resolve command/path: {commandOrPath}");
    }

    private static IEnumerable<string> EnumerateCandidates(string commandOrPath, IReadOnlyList<string> pathExtensions)
    {
        yield return commandOrPath;

        if (Path.HasExtension(commandOrPath))
        {
            yield break;
        }

        foreach (var extension in pathExtensions)
        {
            yield return commandOrPath + extension;
        }
    }
}

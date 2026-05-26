using TeamsRelay.Core;

namespace TeamsRelay.App;

public sealed partial class CliApplication
{
    private static RunOptions ParseRunOptions(IReadOnlyList<string> args, bool allowBackground, string commandName)
    {
        var options = new RunOptions();

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "-d":
                case "--device-name":
                    if (index + 1 >= args.Count)
                    {
                        throw new CliException($"{token} requires a value.");
                    }

                    options.DeviceNames.Add(args[++index]);
                    break;
                case "--device-id":
                    if (index + 1 >= args.Count)
                    {
                        throw new CliException($"{token} requires a value.");
                    }

                    options.DeviceIds.Add(args[++index]);
                    break;
                case "--config":
                    if (index + 1 >= args.Count)
                    {
                        throw new CliException($"{token} requires a value.");
                    }

                    options.ConfigPath = args[++index];
                    break;
                case "--background" when allowBackground:
                    options.Background = true;
                    break;
                default:
                    throw new CliException($"Unknown {commandName} argument: {token}");
            }
        }

        return options;
    }

    private static StopOptions ParseStopOptions(IReadOnlyList<string> args)
    {
        var options = new StopOptions();
        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--timeout-seconds":
                    if (index + 1 >= args.Count || !int.TryParse(args[++index], out var timeout))
                    {
                        throw new CliException("--timeout-seconds requires an integer value.");
                    }

                    options.TimeoutSeconds = timeout;
                    break;
                case "--force":
                    options.Force = true;
                    break;
                default:
                    throw new CliException($"Unknown stop argument: {token}");
            }
        }

        return options;
    }

    private static string? ParseOptionalConfigPath(IReadOnlyList<string> args, string commandName)
    {
        if (args.Count == 0)
        {
            return null;
        }

        if (args.Count == 2 && args[0] == "--config")
        {
            return args[1];
        }

        throw new CliException($"{commandName} only supports optional '--config <path>'.");
    }

    private sealed class RunOptions
    {
        public List<string> DeviceNames { get; } = [];

        public List<string> DeviceIds { get; } = [];

        public string? ConfigPath { get; set; }

        public bool Background { get; set; }

        public bool ShowHelp { get; set; }
    }

    private sealed class StopOptions
    {
        public int TimeoutSeconds { get; set; } = 8;

        public bool Force { get; set; }

        public bool ShowHelp { get; set; }
    }
}

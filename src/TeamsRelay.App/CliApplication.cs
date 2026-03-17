using TeamsRelay.Core;
using TeamsRelay.Target.KdeConnect;

namespace TeamsRelay.App;

public sealed partial class CliApplication
{
    private static readonly string[] UsageLines =
    [
        "Usage:",
        "  teamsrelay run [--device-name <name>]... [--device-id <id>]... [--config <path>]",
        "  teamsrelay start [--device-name <name>]... [--device-id <id>]... [--config <path>]",
        "  teamsrelay stop [--timeout-seconds <n>] [--force]",
        "  teamsrelay status",
        "  teamsrelay devices [--config <path>]",
        "  teamsrelay logs [--follow]",
        "  teamsrelay doctor [--config <path>]",
        "  teamsrelay config init [--path <path>] [--force]",
        "",
        "Root flags:",
        "  teamsrelay --help | -h",
        "  teamsrelay --version | -v",
        "",
        "Alias: tr.cmd supports the same verbs and flags."
    ];

    private readonly TextWriter stderr;
    private readonly TextReader stdin;
    private readonly TextWriter stdout;
    private readonly AppEnvironment environment;
    private readonly ConfigFileService configFiles;
    private readonly RelayStateStore stateStore;
    private readonly IRelayTargetAdapter targetAdapter;
    private readonly IProcessOperations processOperations;

    public CliApplication(TextWriter stdout, TextWriter stderr)
        : this(Console.In, stdout, stderr, AppEnvironment.Detect())
    {
    }

    public CliApplication(TextWriter stdout, TextWriter stderr, AppEnvironment environment)
        : this(Console.In, stdout, stderr, environment)
    {
    }

    public CliApplication(TextReader stdin, TextWriter stdout, TextWriter stderr, AppEnvironment environment)
        : this(stdin, stdout, stderr, environment, new KdeConnectTargetAdapter(environment), new RuntimeProcessOperations())
    {
    }

    internal CliApplication(
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        AppEnvironment environment,
        IRelayTargetAdapter targetAdapter,
        IProcessOperations processOperations)
    {
        this.stdin = stdin;
        this.stdout = stdout;
        this.stderr = stderr;
        this.environment = environment;
        this.targetAdapter = targetAdapter;
        this.processOperations = processOperations;
        configFiles = new ConfigFileService(environment);
        stateStore = new RelayStateStore(RelayRuntimePaths.Create(environment));
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (args.Count == 0)
            {
                WriteUsage(stdout);
                return 1;
            }

            if (IsHelpToken(args[0]))
            {
                WriteUsage(stdout);
                return 0;
            }

            if (IsVersionToken(args[0]))
            {
                stdout.WriteLine($"teamsrelay {ApplicationVersion.Value}");
                return 0;
            }

            return await DispatchCommandAsync(args, cancellationToken);
        }
        catch (CliException exception)
        {
            stderr.WriteLine($"Error: {exception.Message}");
            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }

    private Task<int> DispatchCommandAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var command = args[0].Trim().ToLowerInvariant();
        var remaining = args.Skip(1).ToArray();

        return command switch
        {
            "run" => HandleRunAsync(remaining, cancellationToken),
            "start" => HandleStartAsync(remaining, cancellationToken),
            "stop" => HandleStopAsync(remaining, cancellationToken),
            "status" => Task.FromResult(HandleStatus(remaining)),
            "devices" => HandleDevicesAsync(remaining, cancellationToken),
            "logs" => HandleLogsAsync(remaining, cancellationToken),
            "doctor" => HandleDoctorAsync(remaining, cancellationToken),
            "config" => HandleConfigAsync(remaining, cancellationToken),
            _ => Task.FromResult(WriteUnknownCommand(args[0]))
        };
    }

    private int WriteUnknownCommand(string command)
    {
        throw new CliException($"Unknown command: {command}");
    }

    private static bool IsHelpToken(string value) => value is "-h" or "--help";

    private static bool IsVersionToken(string value) => value is "-v" or "--version";

    private static void WriteUsage(TextWriter writer)
    {
        foreach (var line in UsageLines)
        {
            writer.WriteLine(line);
        }
    }
}

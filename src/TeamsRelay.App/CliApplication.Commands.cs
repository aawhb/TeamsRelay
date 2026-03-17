using System.Diagnostics;
using TeamsRelay.Core;
using TeamsRelay.Source.TeamsUiAutomation;

namespace TeamsRelay.App;

public sealed partial class CliApplication
{
    private const int BackgroundStartDelayMilliseconds = 900;

    private async Task<int> HandleConfigAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            throw new CliException("config requires a subcommand.");
        }

        if (IsHelpToken(args[0]))
        {
            stdout.WriteLine("Usage: teamsrelay config init [--path <path>] [--force]");
            return 0;
        }

        return args[0].Trim().ToLowerInvariant() switch
        {
            "init" => await HandleConfigInitAsync(args.Skip(1).ToArray(), cancellationToken),
            _ => throw new CliException($"Unknown command: config {args[0]}")
        };
    }

    private async Task<int> HandleConfigInitAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        string? path = null;
        var force = false;

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "-h":
                case "--help":
                    stdout.WriteLine("Usage: teamsrelay config init [--path <path>] [--force]");
                    return 0;
                case "--path":
                    if (index + 1 >= args.Count)
                    {
                        throw new CliException("--path requires a value.");
                    }

                    path = args[++index];
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new CliException($"Unknown config init argument: {token}");
            }
        }

        var writtenPath = await configFiles.InitializeAsync(path, force, cancellationToken);
        stdout.WriteLine($"Config initialized: {writtenPath}");
        return 0;
    }

    private async Task<int> HandleRunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var options = ParseRunOptions(args, allowBackground: true, "run");
        if (options.ShowHelp)
        {
            stdout.WriteLine("Usage: teamsrelay run [--device-name <name>]... [--device-id <id>]... [--config <path>]");
            return 0;
        }

        var resolution = await ResolveRunContextAsync(options, interactiveAllowed: !options.Background, cancellationToken);
        using var sourceAdapter = new TeamsUiAutomationSourceAdapter(
            resolution.Config.Config.Source.CaptureMode,
            enableDiagnostics: string.Equals(
                resolution.Config.Config.Runtime.LogLevel,
                "debug",
                StringComparison.OrdinalIgnoreCase));
        var runner = new RelayRunner(
            environment,
            resolution.Config.Config,
            sourceAdapter,
            targetAdapter,
            new RuntimeProcessNameResolver(),
            resolution.SelectedDevices.Select(device => device.Id).ToArray());

        if (!options.Background)
        {
            stdout.WriteLine("TeamsRelay is running.");
            stdout.WriteLine("Press Ctrl+C to stop.");
            stdout.WriteLine();
        }

        await runner.RunAsync(cancellationToken);
        return 0;
    }

    private async Task<int> HandleStartAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var options = ParseRunOptions(args, allowBackground: false, "start");
        if (options.ShowHelp)
        {
            stdout.WriteLine("Usage: teamsrelay start [--device-name <name>]... [--device-id <id>]... [--config <path>]");
            return 0;
        }

        var snapshot = stateStore.Read();
        if (snapshot.ProcessState == RelayProcessState.Running)
        {
            throw new CliException($"Relay is already running (pid={snapshot.ProcessId}). Use 'teamsrelay stop' first.");
        }

        if (snapshot.ProcessState == RelayProcessState.Stale)
        {
            stateStore.Clear();
        }

        var resolution = await ResolveRunContextAsync(options, interactiveAllowed: true, cancellationToken);
        var invoker = SelfProcessInvoker.Detect();
        var startInfo = invoker.CreateBackgroundRunStartInfo(
            environment.RootDirectory,
            resolution.Config.Path,
            resolution.SelectedDevices.Select(device => device.Id).ToArray());
        using var process = Process.Start(startInfo) ?? throw new CliException("Failed to start relay process.");

        await Task.Delay(TimeSpan.FromMilliseconds(BackgroundStartDelayMilliseconds), cancellationToken);
        process.Refresh();
        if (process.HasExited)
        {
            throw new CliException($"Relay failed to start (exit={process.ExitCode}).");
        }

        await stateStore.WriteAsync(new RelayInstanceMetadata
        {
            ProcessId = process.Id,
            StartedAtUtc = DateTimeOffset.UtcNow,
            ConfigPath = resolution.Config.Path,
            Version = ApplicationVersion.Value,
            DeviceNames = resolution.SelectedDevices.Select(device => device.Name).ToArray()
        }, cancellationToken);

        stdout.WriteLine($"Relay started in background (pid={process.Id}).");
        return 0;
    }

    private async Task<int> HandleStopAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var options = ParseStopOptions(args);
        if (options.ShowHelp)
        {
            stdout.WriteLine("Usage: teamsrelay stop [--timeout-seconds <n>] [--force]");
            return 0;
        }

        var snapshot = stateStore.Read();
        if (snapshot.ProcessState == RelayProcessState.Stopped)
        {
            var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: null);
            stdout.WriteLine(auxiliaryStopped > 0
                ? FormatAuxiliaryStopMessage(auxiliaryStopped)
                : "Relay is not running.");
            return 0;
        }

        if (snapshot.ProcessState == RelayProcessState.Stale || snapshot.ProcessId is null)
        {
            stateStore.Clear();
            var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: null);
            stdout.WriteLine(auxiliaryStopped > 0
                ? $"Relay is not running (stale state cleared). {FormatAuxiliaryStopMessage(auxiliaryStopped)}"
                : "Relay is not running (stale state cleared).");
            return 0;
        }

        var stopPath = stateStore.Paths.StopFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(stopPath)!);
        await File.WriteAllTextAsync(stopPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

        if (options.Force)
        {
            processOperations.Kill(snapshot.ProcessId.Value);
            stateStore.Clear();
            var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: snapshot.ProcessId.Value);
            stdout.WriteLine(FormatStopMessage("Relay stopped (forced).", auxiliaryStopped));
            return 0;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.TimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!processOperations.IsRunning(snapshot.ProcessId.Value))
            {
                stateStore.Clear();
                var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: snapshot.ProcessId.Value);
                stdout.WriteLine(FormatStopMessage("Relay stopped.", auxiliaryStopped));
                return 0;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        processOperations.Kill(snapshot.ProcessId.Value);
        stateStore.Clear();
        var forceFallbackAuxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: snapshot.ProcessId.Value);
        stdout.WriteLine(FormatStopMessage("Relay stopped (force fallback used).", forceFallbackAuxiliaryStopped));
        return 0;
    }

    private int HandleStatus(IReadOnlyList<string> args)
    {
        if (args.Count > 0)
        {
            throw new CliException("status does not accept arguments.");
        }

        var snapshot = stateStore.Read();

        switch (snapshot.ProcessState)
        {
            case RelayProcessState.Running:
                stdout.WriteLine("Status: running");
                stdout.WriteLine($"PID: {snapshot.ProcessId}");
                if (snapshot.Metadata is not null)
                {
                    stdout.WriteLine($"Started: {snapshot.Metadata.StartedAtUtc:O}");
                    if (snapshot.Metadata.DeviceNames.Length > 0)
                    {
                        stdout.WriteLine($"Devices: {string.Join(", ", snapshot.Metadata.DeviceNames)}");
                    }
                    stdout.WriteLine($"Config: {snapshot.Metadata.ConfigPath}");
                    stdout.WriteLine($"Version: {snapshot.Metadata.Version}");
                }
                return 0;
            case RelayProcessState.Stale:
                stdout.WriteLine("Status: stale state (cleaning)");
                stateStore.Clear();
                return 0;
            default:
                stdout.WriteLine("Status: stopped");
                return 0;
        }
    }

    private async Task<int> HandleDevicesAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 1 && IsHelpToken(args[0]))
        {
            stdout.WriteLine("Usage: teamsrelay devices [--config <path>]");
            return 0;
        }

        var configPath = ParseOptionalConfigPath(args, "devices");
        var config = await configFiles.LoadAsync(configPath, cancellationToken);
        var devices = await targetAdapter.GetDeviceInventoryAsync(config.Config, cancellationToken);

        if (devices.Count == 0)
        {
            stdout.WriteLine("No paired devices found.");
            return 0;
        }

        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var status = device.IsAvailable ? "online" : "offline";
            stdout.WriteLine($"[{index + 1}] {device.Name} ({device.Id}) [{status}]");
        }

        return 0;
    }

    private async Task<int> HandleLogsAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var follow = false;
        foreach (var token in args)
        {
            switch (token)
            {
                case "-h":
                case "--help":
                    stdout.WriteLine("Usage: teamsrelay logs [--follow]");
                    return 0;
                case "--follow":
                    follow = true;
                    break;
                default:
                    throw new CliException($"Unknown logs argument: {token}");
            }
        }

        var latestLogPath = JsonLogWriter.FindLatestLogPath(stateStore.Paths.LogsDirectory);
        if (latestLogPath is null)
        {
            stdout.WriteLine($"No relay log files found in: {stateStore.Paths.LogsDirectory}");
            return 0;
        }

        var logWriter = new JsonLogWriter(latestLogPath);
        var lines = logWriter.ReadTail();
        foreach (var line in lines)
        {
            stdout.WriteLine(line);
        }

        if (!follow)
        {
            return 0;
        }

        using var stream = new FileStream(logWriter.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(0, SeekOrigin.End);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                stdout.WriteLine(line);
                continue;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        return 0;
    }

    private async Task<int> HandleDoctorAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 1 && IsHelpToken(args[0]))
        {
            stdout.WriteLine("Usage: teamsrelay doctor [--config <path>]");
            return 0;
        }

        var configPath = ParseOptionalConfigPath(args, "doctor");
        var config = await configFiles.LoadAsync(configPath, cancellationToken);
        var checks = new List<RelayDiagnosticCheck>
        {
            new()
            {
                Name = "config_valid",
                Passed = true,
                Details = config.Path
            }
        };

        checks.AddRange(RunProcessDiagnostics());

        var report = await targetAdapter.RunDoctorAsync(config.Config, cancellationToken);
        checks.AddRange(report.Checks);

        foreach (var check in checks)
        {
            var status = check.Passed
                ? "PASS"
                : check.IsBlocking ? "FAIL" : "WARN";
            stdout.WriteLine($"[{status}] {check.Name}: {check.Details}");
        }

        if (report.HasBlockingFailures)
        {
            throw new CliException("Doctor failed.");
        }

        stdout.WriteLine("Doctor passed.");
        return 0;
    }

    private IReadOnlyList<RelayDiagnosticCheck> RunProcessDiagnostics()
    {
        var checks = new List<RelayDiagnosticCheck>();

        var snapshot = stateStore.Read();
        var stateLabel = snapshot.ProcessState switch
        {
            RelayProcessState.Running => $"running (pid={snapshot.ProcessId})",
            RelayProcessState.Stale => $"stale (pid={snapshot.ProcessId} no longer alive)",
            _ => "stopped"
        };
        checks.Add(new RelayDiagnosticCheck
        {
            Name = "relay_state",
            Passed = snapshot.ProcessState != RelayProcessState.Stale,
            IsBlocking = false,
            Details = stateLabel
        });

        var repoLocalProcesses = processOperations.FindRepoLocalTeamsRelayProcesses(environment.RootDirectory);
        var count = repoLocalProcesses.Count;
        var details = count == 0
            ? "none"
            : string.Join(", ", repoLocalProcesses.Select(p => $"pid={p.ProcessId} ({p.Name})"));
        checks.Add(new RelayDiagnosticCheck
        {
            Name = "relay_process_count",
            Passed = count <= 1,
            IsBlocking = false,
            Details = $"{count} repo-local process(es): {details}"
        });

        return checks;
    }

    private int StopRepoLocalTeamsRelayProcesses(int? excludeProcessId)
    {
        var stopped = 0;
        foreach (var process in processOperations.FindRepoLocalTeamsRelayProcesses(environment.RootDirectory)
                     .DistinctBy(process => process.ProcessId))
        {
            if (process.ProcessId == processOperations.CurrentProcessId)
            {
                continue;
            }

            if (excludeProcessId is not null && process.ProcessId == excludeProcessId.Value)
            {
                continue;
            }

            processOperations.Kill(process.ProcessId);
            stopped++;
        }

        return stopped;
    }

    private static string FormatStopMessage(string baseMessage, int auxiliaryStopped)
    {
        return auxiliaryStopped > 0
            ? $"{baseMessage} {FormatAuxiliaryStopMessage(auxiliaryStopped)}"
            : baseMessage;
    }

    private static string FormatAuxiliaryStopMessage(int auxiliaryStopped)
    {
        return auxiliaryStopped == 1
            ? "Stopped 1 local TeamsRelay process."
            : $"Stopped {auxiliaryStopped} local TeamsRelay processes.";
    }
}

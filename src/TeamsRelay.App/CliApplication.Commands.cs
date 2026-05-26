using TeamsRelay.Core;
using TeamsRelay.Lifecycle;
using TeamsRelay.Source.TeamsUiAutomation;

namespace TeamsRelay.App;

public sealed partial class CliApplication
{
    private const int BackgroundStartDelayMilliseconds = 900;

    private async Task<int> HandleRunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var options = ParseRunOptions(args, allowBackground: true, "run");
        if (options.ShowHelp)
        {
            _stdout.WriteLine("Usage: teamsrelay run [--device-name <name>]... [--device-id <id>]... [--config <path>]");
            return 0;
        }

        var resolution = await ResolveRunContextAsync(options, interactiveAllowed: !options.Background, cancellationToken);
        var processNameResolver = new RuntimeProcessNameResolver();
        using var sourceAdapter = new TeamsUiAutomationSourceAdapter(processNameResolver);
        var runner = new RelayRunner(
            _environment,
            resolution.Config.Config,
            sourceAdapter,
            _targetAdapter,
            processNameResolver,
            resolution.SelectedDevices.Select(device => device.Id).ToArray());

        if (!options.Background)
        {
            _stdout.WriteLine("TeamsRelay is running.");
            _stdout.WriteLine("Press Ctrl+C to stop.");
            _stdout.WriteLine();
        }

        await runner.RunAsync(cancellationToken);
        return 0;
    }

    private async Task<int> HandleStartAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var options = ParseRunOptions(args, allowBackground: false, "start");
        if (options.ShowHelp)
        {
            _stdout.WriteLine("Usage: teamsrelay start [--device-name <name>]... [--device-id <id>]... [--config <path>]");
            return 0;
        }

        var snapshot = _stateStore.Read();
        if (snapshot.ProcessState == LifecycleState.Running)
        {
            throw new CliException($"Relay is already running (pid={snapshot.ProcessId}). Use 'teamsrelay stop' first.");
        }

        if (snapshot.ProcessState == LifecycleState.Stale)
        {
            _stateStore.Clear();
        }

        var resolution = await ResolveRunContextAsync(options, interactiveAllowed: true, cancellationToken);
        var (executablePath, bootstrapArguments) = BackgroundLaunchPlanner.DetectHost();
        var startInfo = BackgroundLaunchPlanner.Plan(
            executablePath,
            bootstrapArguments,
            _environment.RootDirectory,
            resolution.Config.Path,
            resolution.SelectedDevices.Select(device => device.Id).ToArray());
        IProcessHandle process;
        try
        {
            process = _processOperations.Spawn(startInfo);
        }
        catch (InvalidOperationException exception)
        {
            throw new CliException($"Failed to start relay process: {exception.Message}");
        }

        using (process)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(BackgroundStartDelayMilliseconds), cancellationToken);
            process.Refresh();
            if (process.HasExited)
            {
                throw new CliException($"Relay failed to start (exit={process.ExitCode}).");
            }

            await _stateStore.WriteAsync(new InstanceMetadata
            {
                ProcessId = process.Id,
                StartedAtUtc = DateTimeOffset.UtcNow,
                ConfigPath = resolution.Config.Path,
                Version = ApplicationVersion.Value,
                DeviceNames = resolution.SelectedDevices.Select(device => device.Name).ToArray()
            }, cancellationToken);

            _stdout.WriteLine($"Relay started in background (pid={process.Id}).");
            return 0;
        }
    }

    private async Task<int> HandleStopAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var options = ParseStopOptions(args);
        if (options.ShowHelp)
        {
            _stdout.WriteLine("Usage: teamsrelay stop [--timeout-seconds <n>] [--force]");
            return 0;
        }

        var snapshot = _stateStore.Read();
        if (snapshot.ProcessState == LifecycleState.Stopped)
        {
            var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: null);
            _stdout.WriteLine(auxiliaryStopped > 0
                ? FormatAuxiliaryStopMessage(auxiliaryStopped)
                : "Relay is not running.");
            return 0;
        }

        if (snapshot.ProcessState == LifecycleState.Stale || snapshot.ProcessId is null)
        {
            _stateStore.Clear();
            var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: null);
            _stdout.WriteLine(auxiliaryStopped > 0
                ? $"Relay is not running (stale state cleared). {FormatAuxiliaryStopMessage(auxiliaryStopped)}"
                : "Relay is not running (stale state cleared).");
            return 0;
        }

        var stopPath = _stateStore.Paths.StopFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(stopPath)!);
        await File.WriteAllTextAsync(stopPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

        if (options.Force)
        {
            _processOperations.Kill(snapshot.ProcessId.Value);
            _stateStore.Clear();
            var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: snapshot.ProcessId.Value);
            _stdout.WriteLine(FormatStopMessage("Relay stopped (forced).", auxiliaryStopped));
            return 0;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.TimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!_processOperations.IsRunning(snapshot.ProcessId.Value))
            {
                _stateStore.Clear();
                var auxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: snapshot.ProcessId.Value);
                _stdout.WriteLine(FormatStopMessage("Relay stopped.", auxiliaryStopped));
                return 0;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        _processOperations.Kill(snapshot.ProcessId.Value);
        _stateStore.Clear();
        var forceFallbackAuxiliaryStopped = StopRepoLocalTeamsRelayProcesses(excludeProcessId: snapshot.ProcessId.Value);
        _stdout.WriteLine(FormatStopMessage("Relay stopped (force fallback used).", forceFallbackAuxiliaryStopped));
        return 0;
    }

    private int HandleStatus(IReadOnlyList<string> args)
    {
        if (args.Count > 0)
        {
            throw new CliException("status does not accept arguments.");
        }

        var snapshot = _stateStore.Read();

        switch (snapshot.ProcessState)
        {
            case LifecycleState.Running:
                _stdout.WriteLine("Status: running");
                _stdout.WriteLine($"PID: {snapshot.ProcessId}");
                if (snapshot.Metadata is not null)
                {
                    _stdout.WriteLine($"Started: {snapshot.Metadata.StartedAtUtc:O}");
                    if (snapshot.Metadata.DeviceNames.Length > 0)
                    {
                        _stdout.WriteLine($"Devices: {string.Join(", ", snapshot.Metadata.DeviceNames)}");
                    }
                    _stdout.WriteLine($"Config: {snapshot.Metadata.ConfigPath}");
                    _stdout.WriteLine($"Version: {snapshot.Metadata.Version}");
                }
                return 0;
            case LifecycleState.Stale:
                _stdout.WriteLine("Status: stale state (cleaning)");
                _stateStore.Clear();
                return 0;
            default:
                _stdout.WriteLine("Status: stopped");
                return 0;
        }
    }

    private async Task<int> HandleDevicesAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 1 && IsHelpToken(args[0]))
        {
            _stdout.WriteLine("Usage: teamsrelay devices [--config <path>]");
            return 0;
        }

        var configPath = ParseOptionalConfigPath(args, "devices");
        var config = await _configFiles.LoadAsync(configPath, cancellationToken);
        var devices = await _targetAdapter.GetDeviceInventoryAsync(config.Config, cancellationToken);

        if (devices.Count == 0)
        {
            _stdout.WriteLine("No paired devices found.");
            return 0;
        }

        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var status = device.IsAvailable ? "online" : "offline";
            _stdout.WriteLine($"[{index + 1}] {device.Name} ({device.Id}) [{status}]");
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
                    _stdout.WriteLine("Usage: teamsrelay logs [--follow]");
                    return 0;
                case "--follow":
                    follow = true;
                    break;
                default:
                    throw new CliException($"Unknown logs argument: {token}");
            }
        }

        var latestLogPath = JsonLogWriter.FindLatestLogPath(_stateStore.Paths.LogsDirectory);
        if (latestLogPath is null)
        {
            _stdout.WriteLine($"No relay log files found in: {_stateStore.Paths.LogsDirectory}");
            return 0;
        }

        var logWriter = new JsonLogWriter(latestLogPath);
        var lines = logWriter.ReadTail();
        foreach (var line in lines)
        {
            _stdout.WriteLine(line);
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
                _stdout.WriteLine(line);
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
            _stdout.WriteLine("Usage: teamsrelay doctor [--config <path>]");
            return 0;
        }

        var configPath = ParseOptionalConfigPath(args, "doctor");
        var config = await _configFiles.LoadAsync(configPath, cancellationToken);
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

        var report = await _targetAdapter.RunDoctorAsync(config.Config, cancellationToken);
        checks.AddRange(report.Checks);

        foreach (var check in checks)
        {
            var status = check.Passed
                ? "PASS"
                : check.IsBlocking ? "FAIL" : "WARN";
            _stdout.WriteLine($"[{status}] {check.Name}: {check.Details}");
        }

        if (report.HasBlockingFailures)
        {
            throw new CliException("Doctor failed.");
        }

        _stdout.WriteLine("Doctor passed.");
        return 0;
    }

    private IReadOnlyList<RelayDiagnosticCheck> RunProcessDiagnostics()
    {
        var checks = new List<RelayDiagnosticCheck>();

        var snapshot = _stateStore.Read();
        var stateLabel = snapshot.ProcessState switch
        {
            LifecycleState.Running => $"running (pid={snapshot.ProcessId})",
            LifecycleState.Stale => $"stale (pid={snapshot.ProcessId} no longer alive)",
            _ => "stopped"
        };
        checks.Add(new RelayDiagnosticCheck
        {
            Name = "relay_state",
            Passed = snapshot.ProcessState != LifecycleState.Stale,
            IsBlocking = false,
            Details = stateLabel
        });

        var repoLocalProcesses = _processOperations.FindRepoLocalTeamsRelayProcesses(_environment.RootDirectory);
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
        foreach (var process in _processOperations.FindRepoLocalTeamsRelayProcesses(_environment.RootDirectory)
                     .DistinctBy(process => process.ProcessId))
        {
            if (process.ProcessId == _processOperations.CurrentProcessId)
            {
                continue;
            }

            if (excludeProcessId is not null && process.ProcessId == excludeProcessId.Value)
            {
                continue;
            }

            _processOperations.Kill(process.ProcessId);
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

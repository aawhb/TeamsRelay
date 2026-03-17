using TeamsRelay.Core;

namespace TeamsRelay.Target.KdeConnect;

public sealed class KdeConnectTargetAdapter : IRelayTargetAdapter
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(350);

    private readonly AppEnvironment environment;
    private readonly IKdeCommandRunner runner;

    public KdeConnectTargetAdapter(AppEnvironment environment, IKdeCommandRunner? runner = null)
    {
        this.environment = environment;
        this.runner = runner ?? new ProcessKdeCommandRunner();
    }

    public string Kind => "kde_connect";

    public async Task<IReadOnlyList<RelayDevice>> GetDeviceInventoryAsync(RelayConfig config, CancellationToken cancellationToken = default)
    {
        var executablePath = CommandLocator.Resolve(environment, config.Target.KdeCliPath);
        var pairedDevices = await ListDevicesAsync(executablePath, availableOnly: false, cancellationToken);
        if (pairedDevices.Count == 0)
        {
            return [];
        }

        var availableDevices = await ListDevicesAsync(executablePath, availableOnly: true, cancellationToken);
        var availableIds = availableDevices
            .Select(device => device.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return pairedDevices
            .Select(device => new RelayDevice
            {
                Id = device.Id,
                Name = device.Name,
                IsAvailable = availableIds.Contains(device.Id)
            })
            .ToArray();
    }

    public async Task<RelayDiagnosticReport> RunDoctorAsync(RelayConfig config, CancellationToken cancellationToken = default)
    {
        var checks = new List<RelayDiagnosticCheck>();
        var runtimePaths = RelayRuntimePaths.Create(environment);
        try
        {
            runtimePaths.EnsureDirectories();
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "runtime_dirs_writable",
                Passed = true,
                Details = runtimePaths.RuntimeDirectory
            });
        }
        catch (Exception exception)
        {
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "runtime_dirs_writable",
                Passed = false,
                Details = exception.Message
            });
        }

        string executablePath;
        try
        {
            executablePath = CommandLocator.Resolve(environment, config.Target.KdeCliPath);
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "kde_cli_resolution",
                Passed = true,
                Details = executablePath
            });
        }
        catch (CliException exception)
        {
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "kde_cli_resolution",
                Passed = false,
                Details = exception.Message
            });

            return new RelayDiagnosticReport { Checks = checks };
        }

        IReadOnlyList<RelayDevice> inventory;
        try
        {
            inventory = await GetDeviceInventoryAsync(config, cancellationToken);
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "kde_daemon_responsive",
                Passed = true,
                Details = "kdeconnect-cli list succeeded"
            });
        }
        catch (Exception exception)
        {
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "kde_daemon_responsive",
                Passed = false,
                Details = exception.Message
            });

            return new RelayDiagnosticReport { Checks = checks };
        }

        checks.Add(new RelayDiagnosticCheck
        {
            Name = "paired_devices",
            Passed = inventory.Count > 0,
            Details = $"count={inventory.Count}"
        });

        var availableCount = inventory.Count(device => device.IsAvailable);
        checks.Add(new RelayDiagnosticCheck
        {
            Name = "available_devices",
            Passed = availableCount > 0,
            Details = $"count={availableCount}"
        });

        var configuredIds = config.Target.DeviceIds;
        if (configuredIds.Length == 0)
        {
            checks.Add(new RelayDiagnosticCheck
            {
                Name = "target_selection",
                Passed = false,
                Details = "No target devices configured.",
                IsBlocking = false
            });
        }
        else
        {
            var devicesById = inventory.ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var deviceId in configuredIds)
            {
                checks.Add(new RelayDiagnosticCheck
                {
                    Name = $"target_paired:{deviceId}",
                    Passed = devicesById.ContainsKey(deviceId),
                    Details = devicesById.ContainsKey(deviceId) ? "paired" : "not paired",
                    IsBlocking = false
                });

                checks.Add(new RelayDiagnosticCheck
                {
                    Name = $"target_reachable:{deviceId}",
                    Passed = devicesById.TryGetValue(deviceId, out var device) && device.IsAvailable,
                    Details = devicesById.TryGetValue(deviceId, out device) && device.IsAvailable ? "reachable" : "offline",
                    IsBlocking = false
                });
            }
        }

        return new RelayDiagnosticReport { Checks = checks };
    }

    public async Task SendAsync(RelayConfig config, IReadOnlyList<string> deviceIds, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var executablePath = CommandLocator.Resolve(environment, config.Target.KdeCliPath);
        var failures = new List<string>();
        var attemptedDeviceIds = deviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var deviceId in attemptedDeviceIds)
        {
            var success = false;
            var lastError = string.Empty;

            for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                var result = await runner.RunAsync(executablePath, ["-d", deviceId, "--ping-msg", message], cancellationToken);
                if (result.ExitCode == 0)
                {
                    success = true;
                    break;
                }

                var nonIgnorable = FilterNonIgnorableLines(result.OutputLines);
                if (nonIgnorable.Count == 0)
                {
                    success = true;
                    break;
                }

                lastError = string.Join(' ', nonIgnorable);
                if (attempt < MaxRetryAttempts - 1)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }

            if (!success)
            {
                failures.Add($"{deviceId}: {lastError}");
            }
        }

        if (failures.Count == 0)
        {
            return;
        }

        if (failures.Count == attemptedDeviceIds.Length)
        {
            throw new CliException($"Failed to send KDE ping to all targets: {string.Join(" | ", failures)}");
        }

        throw new PartialTargetSendException(attemptedDeviceIds.Length - failures.Count, failures);
    }

    private async Task<IReadOnlyList<KdeDeviceRecord>> ListDevicesAsync(string executablePath, bool availableOnly, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            var arguments = availableOnly
                ? new[] { "-a", "--id-name-only" }
                : new[] { "-l", "--id-name-only" };
            var result = await runner.RunAsync(executablePath, arguments, cancellationToken);
            var devices = result.OutputLines
                .Select(TryParseDeviceLine)
                .Where(device => device is not null)
                .Cast<KdeDeviceRecord>()
                .ToArray();

            if (devices.Length > 0)
            {
                return devices;
            }

            var nonIgnorable = FilterNonIgnorableLines(result.OutputLines);
            if (result.ExitCode == 0)
            {
                return [];
            }

            if (attempt < MaxRetryAttempts - 1)
            {
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            var detail = nonIgnorable.Count > 0
                ? string.Join(' ', nonIgnorable)
                : "kdeconnect-cli returned only ignorable warnings and no parsed devices.";
            throw new CliException($"Failed to list KDE devices ({(availableOnly ? "available" : "all")}): {detail}");
        }

        return [];
    }

    private static IReadOnlyList<string> FilterNonIgnorableLines(IEnumerable<string> lines)
    {
        return lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !IsIgnorableWarningLine(line))
            .ToArray();
    }

    private static bool IsIgnorableWarningLine(string line)
    {
        return line.Contains("QEventDispatcherWin32::wakeUp: Call to RegisterDeviceNotification failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Invalid window handle", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Failed to post a message", StringComparison.OrdinalIgnoreCase);
    }

    private static KdeDeviceRecord? TryParseDeviceLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || IsIgnorableWarningLine(line))
        {
            return null;
        }

        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var id = parts[0];
        if (!IsLikelyDeviceId(id))
        {
            return null;
        }

        return new KdeDeviceRecord(id, parts[1]);
    }

    private static bool IsLikelyDeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            return false;
        }

        return value.Any(char.IsDigit);
    }

    private sealed record KdeDeviceRecord(string Id, string Name);
}

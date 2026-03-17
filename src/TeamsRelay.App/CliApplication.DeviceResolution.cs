using TeamsRelay.Core;

namespace TeamsRelay.App;

public sealed partial class CliApplication
{
    private async Task<RunResolution> ResolveRunContextAsync(RunOptions options, bool interactiveAllowed, CancellationToken cancellationToken)
    {
        var config = await configFiles.LoadAsync(options.ConfigPath, cancellationToken);
        var inventory = await targetAdapter.GetDeviceInventoryAsync(config.Config, cancellationToken);
        if (inventory.Count == 0)
        {
            throw new CliException("No paired KDE devices found.");
        }

        var selectedDevices = ResolveTargetDevices(
            inventory,
            options.DeviceNames,
            options.DeviceIds,
            config.Config.Target.DeviceIds,
            interactiveAllowed);

        if (selectedDevices.Count == 0)
        {
            throw new CliException("No target devices selected.");
        }

        var offlineDevices = selectedDevices.Where(device => !device.IsAvailable).ToArray();
        if (offlineDevices.Length > 0)
        {
            var offlineText = string.Join("; ", offlineDevices.Select(device => $"{device.Name} ({device.Id})"));
            throw new CliException($"Selected device(s) are currently offline: {offlineText}. Run 'teamsrelay devices' and reconnect in KDE Connect, then retry.");
        }

        return new RunResolution(config, selectedDevices);
    }

    private IReadOnlyList<RelayDevice> ResolveTargetDevices(
        IReadOnlyList<RelayDevice> inventory,
        IReadOnlyList<string> requestedNames,
        IReadOnlyList<string> requestedIds,
        IReadOnlyList<string> configuredIds,
        bool interactiveAllowed)
    {
        var selected = new List<RelayDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var deviceId in requestedIds)
        {
            var match = inventory.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException($"No paired device matches id '{deviceId}'.");
            if (seen.Add(match.Id))
            {
                selected.Add(match);
            }
        }

        foreach (var deviceName in requestedNames)
        {
            var match = ResolveDeviceByName(inventory, deviceName);
            if (seen.Add(match.Id))
            {
                selected.Add(match);
            }
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        foreach (var deviceId in configuredIds)
        {
            var match = inventory.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException($"Configured target device id '{deviceId}' is not paired. Run 'teamsrelay devices' and update target.deviceIds.");
            if (seen.Add(match.Id))
            {
                selected.Add(match);
            }
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        if (!interactiveAllowed)
        {
            throw new CliException("No target devices configured. Set target.deviceIds or use 'teamsrelay start' for interactive selection.");
        }

        return SelectDevicesInteractively(inventory);
    }

    private RelayDevice ResolveDeviceByName(IReadOnlyList<RelayDevice> inventory, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new CliException("Device name query cannot be empty.");
        }

        var normalized = query.Trim();
        var exact = inventory.Where(device => string.Equals(device.Name, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }

        var startsWith = inventory.Where(device => device.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (startsWith.Length == 1)
        {
            return startsWith[0];
        }

        var contains = inventory.Where(device => device.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (contains.Length == 1)
        {
            return contains[0];
        }

        throw new CliException($"No unique device matched '{query}'.");
    }

    private IReadOnlyList<RelayDevice> SelectDevicesInteractively(IReadOnlyList<RelayDevice> inventory)
    {
        stdout.WriteLine("Paired devices:");
        for (var index = 0; index < inventory.Count; index++)
        {
            var device = inventory[index];
            stdout.WriteLine($" [{index + 1}] {device.Name} ({device.Id}) [{(device.IsAvailable ? "online" : "offline")}]");
        }

        while (true)
        {
            stdout.Write("Select device indices (comma/range, e.g. 1,3-4): ");
            var input = stdin.ReadLine();
            if (input is null)
            {
                throw new CliException("Device selection cancelled.");
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                stdout.WriteLine("Selection required.");
                continue;
            }

            try
            {
                var indices = ParseSelectionInput(input, inventory.Count);
                return indices.Select(index => inventory[index - 1]).ToArray();
            }
            catch (CliException exception)
            {
                stdout.WriteLine(exception.Message);
            }
        }
    }

    private static IReadOnlyList<int> ParseSelectionInput(string input, int maxIndex)
    {
        var results = new List<int>();
        var seen = new HashSet<int>();

        foreach (var token in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('-', StringComparison.Ordinal))
            {
                var parts = token.Split('-', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end))
                {
                    throw new CliException($"Invalid selection token: '{token}'. Use comma-separated indices/ranges (e.g., 1,3-4).");
                }

                if (start > end)
                {
                    (start, end) = (end, start);
                }

                for (var index = start; index <= end; index++)
                {
                    if (index < 1 || index > maxIndex)
                    {
                        throw new CliException($"Selection index '{index}' is out of range 1..{maxIndex}.");
                    }

                    if (seen.Add(index))
                    {
                        results.Add(index);
                    }
                }

                continue;
            }

            if (!int.TryParse(token, out var single))
            {
                throw new CliException($"Invalid selection token: '{token}'. Use comma-separated indices/ranges (e.g., 1,3-4).");
            }

            if (single < 1 || single > maxIndex)
            {
                throw new CliException($"Selection index '{single}' is out of range 1..{maxIndex}.");
            }

            if (seen.Add(single))
            {
                results.Add(single);
            }
        }

        return results;
    }

    private sealed record RunResolution(ResolvedRelayConfig Config, IReadOnlyList<RelayDevice> SelectedDevices);
}

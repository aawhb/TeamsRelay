namespace TeamsRelay.Core;

public interface IRelayTargetAdapter
{
    string Kind { get; }

    Task<IReadOnlyList<RelayDevice>> GetDeviceInventoryAsync(RelayConfig config, CancellationToken cancellationToken = default);

    Task<RelayDiagnosticReport> RunDoctorAsync(RelayConfig config, CancellationToken cancellationToken = default);

    Task SendAsync(RelayConfig config, IReadOnlyList<string> deviceIds, string message, CancellationToken cancellationToken = default);
}

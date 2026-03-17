namespace TeamsRelay.Core;

public interface IRelaySourceRuntimeDiagnostics
{
    RelaySourceRuntimeSnapshot GetRuntimeSnapshot();
}

namespace TeamsRelay.Core;

public interface IRelaySourceAdapter : IDisposable
{
    long DroppedCount { get; }

    void Start();

    void Stop();

    bool TryDequeue(out RelaySourceRecord? record);

    bool TryDequeueDiagnostic(out RelaySourceDiagnostic? diagnostic);
}

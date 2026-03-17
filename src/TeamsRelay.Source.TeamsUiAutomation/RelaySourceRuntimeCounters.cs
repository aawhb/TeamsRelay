using System.Threading;
using TeamsRelay.Core;

namespace TeamsRelay.Source.TeamsUiAutomation;

internal sealed class RelaySourceRuntimeCounters
{
    private long candidatesEmitted;
    private long rejectedBroadRect;
    private long rejectedClassifier;
    private long rejectedNotMessageLike;
    private long rejectedNotTeamsProcess;
    private long rejectedNotTeamsWindow;
    private long rejectedProcessNotFound;
    private long rejectedSuppressedContent;
    private long structureChangedSeen;
    private long textExtractionAttempts;
    private long textExtractionFailures;
    private long windowOpenedSeen;

    public void IncrementSeen(string eventKind)
    {
        if (string.Equals(eventKind, "window_opened", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref windowOpenedSeen);
            return;
        }

        if (string.Equals(eventKind, "structure_changed", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref structureChangedSeen);
        }
    }

    public void IncrementTextExtractionAttempt()
    {
        Interlocked.Increment(ref textExtractionAttempts);
    }

    public void IncrementTextExtractionFailure()
    {
        Interlocked.Increment(ref textExtractionFailures);
    }

    public void IncrementCandidatesEmitted(int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref candidatesEmitted, count);
    }

    public void IncrementRejection(string reason)
    {
        switch (reason)
        {
            case "broad_rect_gate":
                Interlocked.Increment(ref rejectedBroadRect);
                break;
            case "process_not_found":
                Interlocked.Increment(ref rejectedProcessNotFound);
                break;
            case "not_teams_process":
                Interlocked.Increment(ref rejectedNotTeamsProcess);
                break;
            case "not_teams_window":
                Interlocked.Increment(ref rejectedNotTeamsWindow);
                break;
            case "classifier_rejected":
                Interlocked.Increment(ref rejectedClassifier);
                break;
            case "suppressed_content":
                Interlocked.Increment(ref rejectedSuppressedContent);
                break;
            case "not_message_like":
                Interlocked.Increment(ref rejectedNotMessageLike);
                break;
        }
    }

    public RelaySourceRuntimeSnapshot CreateSnapshot(
        int queueDepth,
        int diagnosticQueueDepth,
        int pendingCandidateCount,
        long droppedCount)
    {
        return new RelaySourceRuntimeSnapshot
        {
            QueueDepth = queueDepth,
            DiagnosticQueueDepth = diagnosticQueueDepth,
            PendingCandidateCount = pendingCandidateCount,
            DroppedCount = droppedCount,
            WindowOpenedSeen = Interlocked.Read(ref windowOpenedSeen),
            StructureChangedSeen = Interlocked.Read(ref structureChangedSeen),
            TextExtractionAttempts = Interlocked.Read(ref textExtractionAttempts),
            TextExtractionFailures = Interlocked.Read(ref textExtractionFailures),
            CandidatesEmitted = Interlocked.Read(ref candidatesEmitted),
            RejectedBroadRect = Interlocked.Read(ref rejectedBroadRect),
            RejectedProcessNotFound = Interlocked.Read(ref rejectedProcessNotFound),
            RejectedNotTeamsProcess = Interlocked.Read(ref rejectedNotTeamsProcess),
            RejectedNotTeamsWindow = Interlocked.Read(ref rejectedNotTeamsWindow),
            RejectedClassifier = Interlocked.Read(ref rejectedClassifier),
            RejectedSuppressedContent = Interlocked.Read(ref rejectedSuppressedContent),
            RejectedNotMessageLike = Interlocked.Read(ref rejectedNotMessageLike)
        };
    }
}

namespace TeamsRelay.Core;

public sealed record RelaySourceRuntimeSnapshot
{
    public int QueueDepth { get; init; }

    public int DiagnosticQueueDepth { get; init; }

    public int PendingCandidateCount { get; init; }

    public long DroppedCount { get; init; }

    public long WindowOpenedSeen { get; init; }

    public long StructureChangedSeen { get; init; }

    public long TextExtractionAttempts { get; init; }

    public long TextExtractionFailures { get; init; }

    public long CandidatesEmitted { get; init; }

    public long RejectedBroadRect { get; init; }

    public long RejectedProcessNotFound { get; init; }

    public long RejectedNotTeamsProcess { get; init; }

    public long RejectedNotTeamsWindow { get; init; }

    public long RejectedClassifier { get; init; }

    public long RejectedSuppressedContent { get; init; }

    public long RejectedNotMessageLike { get; init; }

    public RelaySourceRuntimeSnapshot CreateIntervalView(RelaySourceRuntimeSnapshot? previous)
    {
        if (previous is null)
        {
            return this;
        }

        return this with
        {
            WindowOpenedSeen = Math.Max(0, WindowOpenedSeen - previous.WindowOpenedSeen),
            StructureChangedSeen = Math.Max(0, StructureChangedSeen - previous.StructureChangedSeen),
            TextExtractionAttempts = Math.Max(0, TextExtractionAttempts - previous.TextExtractionAttempts),
            TextExtractionFailures = Math.Max(0, TextExtractionFailures - previous.TextExtractionFailures),
            CandidatesEmitted = Math.Max(0, CandidatesEmitted - previous.CandidatesEmitted),
            RejectedBroadRect = Math.Max(0, RejectedBroadRect - previous.RejectedBroadRect),
            RejectedProcessNotFound = Math.Max(0, RejectedProcessNotFound - previous.RejectedProcessNotFound),
            RejectedNotTeamsProcess = Math.Max(0, RejectedNotTeamsProcess - previous.RejectedNotTeamsProcess),
            RejectedNotTeamsWindow = Math.Max(0, RejectedNotTeamsWindow - previous.RejectedNotTeamsWindow),
            RejectedClassifier = Math.Max(0, RejectedClassifier - previous.RejectedClassifier),
            RejectedSuppressedContent = Math.Max(0, RejectedSuppressedContent - previous.RejectedSuppressedContent),
            RejectedNotMessageLike = Math.Max(0, RejectedNotMessageLike - previous.RejectedNotMessageLike)
        };
    }
}

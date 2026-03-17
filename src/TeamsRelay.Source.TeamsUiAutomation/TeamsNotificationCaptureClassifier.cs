using TeamsRelay.Core;

namespace TeamsRelay.Source.TeamsUiAutomation;

public sealed class TeamsNotificationCaptureClassifier
{
    private const int PendingWindowMilliseconds = 1500;
    private const double MinimumOverlapRatio = 0.35;
    private const string StructureChangedBannerCapturePath = "structure_changed_banner";

    private readonly string captureMode;
    private readonly List<PendingWindowOpenedCandidate> pendingCandidates = [];

    public TeamsNotificationCaptureClassifier(string captureMode = "strict")
    {
        this.captureMode = NormalizeCaptureMode(captureMode);
    }

    public int PendingCandidateCount => pendingCandidates.Count;

    public IReadOnlyList<RelaySourceRecord> Process(RelaySourceRecord rawRecord)
    {
        ArgumentNullException.ThrowIfNull(rawRecord);

        var ready = new List<RelaySourceRecord>();
        FlushExpiredInternal(rawRecord.TimestampUtc, ready);

        var rawEventKind = NormalizeEventKind(rawRecord);
        if (string.Equals(rawEventKind, "window_opened", StringComparison.OrdinalIgnoreCase))
        {
            HandleWindowOpened(rawRecord, ready);
        }
        else if (string.Equals(rawEventKind, "structure_changed", StringComparison.OrdinalIgnoreCase))
        {
            HandleStructureChanged(rawRecord, ready);
        }

        return ready;
    }

    public IReadOnlyList<RelaySourceRecord> FlushExpired(DateTimeOffset nowUtc)
    {
        var ready = new List<RelaySourceRecord>();
        FlushExpiredInternal(nowUtc, ready);
        return ready;
    }

    private void HandleWindowOpened(RelaySourceRecord rawRecord, List<RelaySourceRecord> ready)
    {
        if (!TeamsNotificationSurfaceClassifier.IsFromNotificationToast(rawRecord))
            return;

        var candidate = CloneCandidate(
            rawRecord,
            eventKind: "window_opened",
            rawEventKind: "window_opened",
            capturePath: "window_opened");

        // Always create a pending candidate so structure_changed has a chance to
        // enrich it with the real notification content. At window_opened time the
        // toast often only has its window title rendered; the actual message text
        // arrives moments later via structure_changed.
        pendingCandidates.Add(new PendingWindowOpenedCandidate(
            candidate,
            rawRecord.TimestampUtc.AddMilliseconds(PendingWindowMilliseconds)));
    }

    private void HandleStructureChanged(RelaySourceRecord rawRecord, List<RelaySourceRecord> ready)
    {
        var textAnalysis = TeamsNotificationTextAnalyzer.Analyze(rawRecord.ExtractedText);
        if (textAnalysis.ContainsTestNotificationSegment)
        {
            ready.Add(CreateTestNotificationCandidate(rawRecord));
            return;
        }

        var bestMatch = FindBestPendingMatch(rawRecord);
        if (bestMatch is not null)
        {
            pendingCandidates.Remove(bestMatch);
            ready.Add(CreateEnrichedCandidate(bestMatch.Candidate, rawRecord));
            return;
        }

        if (TeamsNotificationSurfaceClassifier.IsFromNotificationToast(rawRecord) && ShouldCreateStandaloneBannerCandidate(textAnalysis))
        {
            ready.Add(CloneCandidate(
                rawRecord,
                eventKind: "structure_changed",
                rawEventKind: "structure_changed",
                capturePath: StructureChangedBannerCapturePath));
            return;
        }
    }

    private void FlushExpiredInternal(DateTimeOffset nowUtc, List<RelaySourceRecord> ready)
    {
        foreach (var pending in pendingCandidates
                     .Where(candidate => candidate.ExpiresAtUtc <= nowUtc)
                     .ToArray())
        {
            ready.Add(pending.Candidate);
            pendingCandidates.Remove(pending);
        }
    }

    private PendingWindowOpenedCandidate? FindBestPendingMatch(RelaySourceRecord structureChangedRecord)
    {
        PendingWindowOpenedCandidate? bestMatch = null;
        var bestOverlap = 0d;

        foreach (var pending in pendingCandidates)
        {
            if (pending.ExpiresAtUtc < structureChangedRecord.TimestampUtc)
            {
                continue;
            }

            if (pending.Candidate.ProcessId != structureChangedRecord.ProcessId)
            {
                continue;
            }

            var overlapRatio = CalculateOverlapRatio(pending.Candidate, structureChangedRecord);
            if (overlapRatio < MinimumOverlapRatio || overlapRatio <= bestOverlap)
            {
                continue;
            }

            bestMatch = pending;
            bestOverlap = overlapRatio;
        }

        return bestMatch;
    }

    private static RelaySourceRecord CreateEnrichedCandidate(RelaySourceRecord windowOpenedRecord, RelaySourceRecord structureChangedRecord)
    {
        return windowOpenedRecord with
        {
            TimestampUtc = structureChangedRecord.TimestampUtc,
            EventKind = "window_opened",
            RawEventKind = NormalizeEventKind(structureChangedRecord),
            CapturePath = "window_opened_enriched",
            RootControlType = PreferNonEmpty(structureChangedRecord.RootControlType, windowOpenedRecord.RootControlType),
            AutomationId = PreferNonEmpty(structureChangedRecord.AutomationId, windowOpenedRecord.AutomationId),
            ExtractedText = MergeExtractedText(windowOpenedRecord.ExtractedText, structureChangedRecord.ExtractedText)
        };
    }

    private static RelaySourceRecord CloneCandidate(
        RelaySourceRecord source,
        string eventKind,
        string rawEventKind,
        string capturePath)
    {
        return source with
        {
            EventKind = eventKind,
            RawEventKind = rawEventKind,
            CapturePath = capturePath
        };
    }

    private static RelaySourceRecord CreateTestNotificationCandidate(RelaySourceRecord source)
    {
        return source with
        {
            EventKind = "structure_changed",
            RawEventKind = "structure_changed",
            CapturePath = "structure_changed_test",
            ExtractedText = TeamsNotificationTextAnalyzer.TestNotificationText
        };
    }

    private static string NormalizeCaptureMode(string? value)
    {
        return string.Equals(value, "hybrid", StringComparison.OrdinalIgnoreCase)
            ? "hybrid"
            : "strict";
    }

    private static string NormalizeEventKind(RelaySourceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.RawEventKind))
        {
            return record.RawEventKind.Trim();
        }

        return record.EventKind.Trim();
    }

    private static bool ShouldCreateStandaloneBannerCandidate(
        TeamsNotificationTextAnalysis textAnalysis)
    {
        return textAnalysis.LooksLikeRealMessageBanner;
    }

    private static double CalculateOverlapRatio(RelaySourceRecord left, RelaySourceRecord right)
    {
        if (left.RectEmpty || right.RectEmpty || left.Width <= 0 || left.Height <= 0 || right.Width <= 0 || right.Height <= 0)
        {
            return 0d;
        }

        var overlapLeft = Math.Max(left.Left, right.Left);
        var overlapTop = Math.Max(left.Top, right.Top);
        var overlapRight = Math.Min(left.Left + left.Width, right.Left + right.Width);
        var overlapBottom = Math.Min(left.Top + left.Height, right.Top + right.Height);
        if (overlapRight <= overlapLeft || overlapBottom <= overlapTop)
        {
            return 0d;
        }

        var overlapArea = (overlapRight - overlapLeft) * (overlapBottom - overlapTop);
        var smallerArea = Math.Min(left.Width * left.Height, right.Width * right.Height);
        if (smallerArea <= 0)
        {
            return 0d;
        }

        return overlapArea / smallerArea;
    }

    private static string MergeExtractedText(string first, string second)
    {
        var segments = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendSegments(first, segments, seen);
        AppendSegments(second, segments, seen);

        return string.Join(" | ", segments);
    }

    private static void AppendSegments(string text, List<string> segments, HashSet<string> seen)
    {
        foreach (var segment in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(segment) || !seen.Add(segment))
            {
                continue;
            }

            segments.Add(segment);
        }
    }

    private static string PreferNonEmpty(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }

    private sealed record PendingWindowOpenedCandidate(RelaySourceRecord Candidate, DateTimeOffset ExpiresAtUtc);
}

using System.Text.RegularExpressions;

namespace TeamsRelay.Core;

public sealed class RelayPipeline
{
    private const int CoalesceWindowMilliseconds = 700;
    private const int StrictMinWidth = 220;
    private const int StrictMaxWidth = 650;
    private const int StrictMinHeight = 60;
    private const int StrictMaxHeight = 300;

    private static readonly string[] AllowedProcessNames = ["ms-teams", "msedgewebview2"];
    private static readonly Regex FingerprintTokenPattern = new(
        "[a-z0-9']+",
        RegexOptions.Compiled);
    private static readonly Regex FingerprintDigitsOnlyPattern = new(
        @"^\d+$",
        RegexOptions.Compiled);
    private static readonly HashSet<string> FingerprintStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft",
        "teams",
        "message",
        "preview",
        "web",
        "content",
        "profile",
        "untitled"
    };
    private static readonly Dictionary<string, int> ProcessPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["msedgewebview2"] = 0,
        ["ms-teams"] = 1
    };
    private static readonly Dictionary<string, int> CapturePathPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["window_opened_enriched"] = 0,
        ["window_opened"] = 1,
        ["structure_changed_banner"] = 2,
        ["structure_changed_test"] = 3
    };

    private readonly Dictionary<string, CoalescedDispatch> coalescedByFingerprint = new(StringComparer.OrdinalIgnoreCase);
    private readonly RelayConfig config;
    private readonly NotificationFormatter notificationFormatter = new();
    private readonly IProcessNameResolver processNameResolver;

    public RelayPipeline(RelayConfig config, IProcessNameResolver processNameResolver)
    {
        this.config = config;
        this.processNameResolver = processNameResolver;
    }

    internal int PendingDispatchCount => coalescedByFingerprint.Count;

    public RelayPipelineAddResult Add(RelaySourceRecord record)
    {
        if (!TryCreateDispatch(record, out var dispatch, out var reason))
        {
            return RelayPipelineAddResult.Rejected(reason);
        }

        if (!coalescedByFingerprint.TryGetValue(dispatch.Fingerprint, out var bucket))
        {
            coalescedByFingerprint[dispatch.Fingerprint] = new CoalescedDispatch(dispatch);
            return RelayPipelineAddResult.Accepted();
        }

        bucket.LastSeenUtc = dispatch.SeenUtc;
        if (IsBetterCandidate(dispatch, bucket.BestDispatch))
        {
            bucket.BestDispatch = dispatch;
        }

        return RelayPipelineAddResult.Merged();
    }

    public IReadOnlyList<RelayDispatch> Flush(bool force = false)
    {
        if (coalescedByFingerprint.Count == 0)
        {
            return [];
        }

        var ready = new List<RelayDispatch>();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in coalescedByFingerprint.ToArray())
        {
            if (!force && (now - entry.Value.FirstSeenUtc).TotalMilliseconds < CoalesceWindowMilliseconds)
            {
                continue;
            }

            var dispatch = entry.Value.BestDispatch;
            coalescedByFingerprint.Remove(entry.Key);
            ready.Add(dispatch);
        }

        return ready;
    }

    public bool TryCreateDispatch(RelaySourceRecord record, out RelayDispatch dispatch, out string reason)
    {
        dispatch = null!;
        reason = string.Empty;

        var processName = processNameResolver.TryGetProcessName(record.ProcessId);
        if (string.IsNullOrWhiteSpace(processName))
        {
            reason = "process_not_found";
            return false;
        }

        if (!AllowedProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            reason = "not_banner_candidate";
            return false;
        }

        if (string.Equals(processName, "msedgewebview2", StringComparison.OrdinalIgnoreCase)
            && !record.TopLevelWindowName.Contains("Teams", StringComparison.OrdinalIgnoreCase))
        {
            reason = "not_teams_window";
            return false;
        }

        var capturePath = NormalizeCapturePath(record.CapturePath);
        if (!CapturePathPriority.ContainsKey(capturePath))
        {
            reason = "not_banner_candidate";
            return false;
        }

        var isTestCapture = string.Equals(capturePath, "structure_changed_test", StringComparison.OrdinalIgnoreCase);

        if (!isTestCapture && !IsWithinStrictBounds(record))
        {
            reason = "not_banner_candidate";
            return false;
        }

        if (!isTestCapture
            && !TeamsNotificationSurfaceClassifier.IsFromNotificationToast(record))
        {
            reason = "main_window_event";
            return false;
        }

        var textAnalysis = TeamsNotificationTextAnalyzer.Analyze(record.ExtractedText);
        var resolvedText = ResolveCleanedMessage(capturePath, textAnalysis);
        if (!string.Equals(capturePath, "structure_changed_test", StringComparison.OrdinalIgnoreCase)
            && textAnalysis.ContainsTestNotificationSegment
            && string.IsNullOrWhiteSpace(resolvedText))
        {
            reason = "suppressed_content";
            return false;
        }

        if (ShouldRejectCleanedMessage(resolvedText))
        {
            reason = "suppressed_content";
            return false;
        }

        var parsedNotification = string.Equals(capturePath, "structure_changed_test", StringComparison.OrdinalIgnoreCase)
            ? null
            : textAnalysis.ParsedNotification;
        var messageType = parsedNotification?.MessageType ?? ParsedNotificationMessageType.Unknown;
        if (!ShouldIncludeMessageType(config.Delivery.Filter, messageType))
        {
            reason = $"filtered_by_type:{messageType}";
            return false;
        }

        var hasContent = !string.IsNullOrWhiteSpace(resolvedText);
        string message;
        var fallbackUsed = false;

        if (string.Equals(config.Delivery.Mode, "generic_ping", StringComparison.OrdinalIgnoreCase))
        {
            message = config.Delivery.GenericPingText;
            hasContent = false;
            fallbackUsed = true;
        }
        else if (hasContent)
        {
            var formatResult = notificationFormatter.Format(
                parsedNotification,
                resolvedText,
                config.Delivery.Format,
                config.Delivery.GenericPingText);

            message = formatResult.Message;
            fallbackUsed = formatResult.UsedGenericPing;
            if (fallbackUsed)
            {
                hasContent = false;
            }
        }
        else if (string.Equals(record.EventKind, "window_opened", StringComparison.OrdinalIgnoreCase)
            && LooksLikeBanner(record))
        {
            message = config.Delivery.GenericPingText;
            fallbackUsed = true;
        }
        else
        {
            reason = "no_text";
            return false;
        }

        if (message.Length > config.Delivery.MaxMessageLength)
        {
            message = TextUtilities.TruncateWithEllipsis(message, config.Delivery.MaxMessageLength);
        }

        dispatch = new RelayDispatch
        {
            Message = message,
            Fingerprint = BuildFingerprint(parsedNotification, resolvedText),
            ProcessName = processName,
            WindowName = TeamsNotificationTextAnalyzer.NormalizeVisibleText(record.WindowName),
            EventKind = record.EventKind,
            CapturePath = capturePath,
            MessageExtracted = resolvedText,
            FallbackUsed = fallbackUsed,
            HasContent = hasContent,
            SeenUtc = record.TimestampUtc
        };

        reason = string.Empty;
        return true;
    }

    private static bool LooksLikeBanner(RelaySourceRecord record)
    {
        return !record.RectEmpty && IsWithinStrictBounds(record);
    }

    private static bool IsWithinStrictBounds(RelaySourceRecord record)
    {
        if (record.RectEmpty)
        {
            return false;
        }

        return record.Width is >= StrictMinWidth and <= StrictMaxWidth
            && record.Height is >= StrictMinHeight and <= StrictMaxHeight;
    }

    private static bool IsBetterCandidate(RelayDispatch newDispatch, RelayDispatch currentDispatch)
    {
        if (newDispatch.HasContent != currentDispatch.HasContent)
        {
            return newDispatch.HasContent;
        }

        if (newDispatch.FallbackUsed != currentDispatch.FallbackUsed)
        {
            return !newDispatch.FallbackUsed;
        }

        var newCapturePriority = CapturePathPriority.GetValueOrDefault(newDispatch.CapturePath, 1000);
        var currentCapturePriority = CapturePathPriority.GetValueOrDefault(currentDispatch.CapturePath, 1000);
        if (newCapturePriority != currentCapturePriority)
        {
            return newCapturePriority < currentCapturePriority;
        }

        var newPriority = ProcessPriority.GetValueOrDefault(newDispatch.ProcessName, 1000);
        var currentPriority = ProcessPriority.GetValueOrDefault(currentDispatch.ProcessName, 1000);
        if (newPriority != currentPriority)
        {
            return newPriority < currentPriority;
        }

        return newDispatch.Message.Length > currentDispatch.Message.Length;
    }

    private static string ResolveCleanedMessage(string capturePath, TeamsNotificationTextAnalysis textAnalysis)
    {
        if (string.Equals(capturePath, "structure_changed_test", StringComparison.OrdinalIgnoreCase))
        {
            return TeamsNotificationTextAnalyzer.TestNotificationText;
        }

        if (textAnalysis.ContainsTestNotificationSegment)
        {
            return textAnalysis.CleanedWithoutTestNotification;
        }

        return textAnalysis.CleanedText;
    }

    private static bool ShouldRejectCleanedMessage(string cleanedMessage)
    {
        return TeamsNotificationTextAnalyzer.IsSuppressedContent(cleanedMessage);
    }

    private static bool ShouldIncludeMessageType(DeliveryFilterOptions filter, ParsedNotificationMessageType messageType)
    {
        return messageType switch
        {
            ParsedNotificationMessageType.DirectMessage => filter.DirectMessages,
            ParsedNotificationMessageType.ConversationMessage => filter.ConversationMessages,
            _ => filter.UnknownTypes
        };
    }

    private static string BuildFingerprint(ParsedNotification? parsedNotification, string resolvedText)
    {
        var fingerprintSource = BuildFingerprintSource(parsedNotification, resolvedText);
        var normalized = TeamsNotificationTextAnalyzer.NormalizeVisibleText(fingerprintSource).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var tokens = FingerprintTokenPattern.Matches(normalized)
            .Select(match => match.Value)
            .Where(token => token.Length >= 2)
            .Where(token => !FingerprintStopWords.Contains(token))
            .Where(token => !FingerprintDigitsOnlyPattern.IsMatch(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens.Length == 0 ? normalized : string.Join(' ', tokens);
    }

    private static string BuildFingerprintSource(ParsedNotification? parsedNotification, string resolvedText)
    {
        if (parsedNotification is null)
        {
            return resolvedText;
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(parsedNotification.Sender))
        {
            parts.Add(parsedNotification.Sender);
        }

        if (!string.IsNullOrWhiteSpace(parsedNotification.MessageBody))
        {
            parts.Add(parsedNotification.MessageBody);
        }

        if (!string.IsNullOrWhiteSpace(parsedNotification.ConversationTitle))
        {
            parts.Add(parsedNotification.ConversationTitle);
        }

        return parts.Count == 0 ? resolvedText : string.Join(' ', parts);
    }

    private static string NormalizeCapturePath(string? capturePath)
    {
        return string.IsNullOrWhiteSpace(capturePath)
            ? string.Empty
            : capturePath.Trim();
    }

    private sealed class CoalescedDispatch
    {
        public CoalescedDispatch(RelayDispatch dispatch)
        {
            FirstSeenUtc = dispatch.SeenUtc;
            LastSeenUtc = dispatch.SeenUtc;
            BestDispatch = dispatch;
        }

        public DateTimeOffset FirstSeenUtc { get; }

        public DateTimeOffset LastSeenUtc { get; set; }

        public RelayDispatch BestDispatch { get; set; }
    }
}

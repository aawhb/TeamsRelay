using System.Text;
using System.Text.RegularExpressions;

namespace TeamsRelay.Core;

public static class TeamsNotificationTextAnalyzer
{
    public const string TestNotificationText = "This is a test notification";
    private static readonly Regex PersonNamePattern = new(
        @"^[A-Z][\p{L}'-]+(?: [A-Z][\p{L}'-]+){1,3}$",
        RegexOptions.Compiled);
    private static readonly Regex ZeroWidthCharPattern = new(
        @"[\u200B-\u200D\uFEFF\u2060\u00AD]",
        RegexOptions.Compiled);
    private static readonly Regex HyphenWhitespacePattern = new(
        @"(?<=\w)-\s+(?=\w)",
        RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.Compiled);
    private static readonly Regex MessagePreviewPrefixPattern = new(
        @"^(Message preview\.?\s*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MicrosoftTeamsSuffixPattern = new(
        @"\s*Microsoft Teams$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MicrosoftTeamsWebContentPattern = new(
        @"^(Microsoft Teams(\s*-\s*Web content.*)?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BoilerplateSegmentPattern = new(
        @"^(Untitled|Message preview\.?|Profile \d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DigitsOnlyPattern = new(
        @"^\d+$",
        RegexOptions.Compiled);
    private static readonly Regex DeletedWordPattern = new(
        @"\bdeleted\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FlaggedWordPattern = new(
        @"\bflagged\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PollResultPattern = new(
        @"\b\d+%\s*\(\d+\)",
        RegexOptions.Compiled);
    private static readonly Regex IdentityTokenPattern = new(
        @"[\p{L}\p{N}'&+\-]+",
        RegexOptions.Compiled);
    private static readonly Regex SenderMessagePattern = new(
        @"^[\p{L}\p{N}'&+\- ]{1,80}:\s+\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> TitleConnectorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "at",
        "for",
        "in",
        "of",
        "on",
        "or",
        "the",
        "to",
        "with"
    };
    private static readonly HashSet<string> TitleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "alert",
        "call",
        "chat",
        "interview",
        "meeting",
        "project",
        "release",
        "request",
        "review",
        "status",
        "sync",
        "weekly"
    };
    private static readonly HashSet<string> NonPersonIdentityKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "communities",
        "community",
        "engage",
        "everyone",
        "insights",
        "updates",
        "viva",
        "yammer"
    };

    public static TeamsNotificationTextAnalysis Analyze(string? extractedText)
    {
        var segments = NormalizeTwoSegmentSenderFirst(
            CollapseDuplicatedOverlapTriplets(CleanSegments(extractedText)));
        var containsTestNotificationSegment = segments.Any(IsTestNotificationSegment);
        var segmentsWithoutTestNotification = segments
            .Where(segment => !IsTestNotificationSegment(segment))
            .ToArray();
        var cleanedText = JoinSegments(segments);
        var cleanedWithoutTestNotification = JoinSegments(segmentsWithoutTestNotification);
        var suppressedContent = IsSuppressedContent(cleanedWithoutTestNotification);
        var looksLikeRealMessageBanner = LooksLikeRealMessageBannerCore(
            segmentsWithoutTestNotification,
            cleanedWithoutTestNotification,
            containsTestNotificationSegment);
        var parsedNotification = ParseNotification(segmentsWithoutTestNotification);

        return new TeamsNotificationTextAnalysis(
            cleanedText,
            cleanedWithoutTestNotification,
            segments,
            segmentsWithoutTestNotification,
            containsTestNotificationSegment,
            segments.Length == 1 && containsTestNotificationSegment,
            suppressedContent,
            looksLikeRealMessageBanner,
            parsedNotification);
    }

    public static string NormalizeVisibleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        normalized = ZeroWidthCharPattern.Replace(normalized, string.Empty);
        normalized = HyphenWhitespacePattern.Replace(normalized, string.Empty);
        normalized = WhitespacePattern.Replace(normalized, " ");
        return normalized.Trim();
    }

    public static bool IsTestNotificationSegment(string? segment)
    {
        return string.Equals(
            NormalizeVisibleText(segment),
            TestNotificationText,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string[] CleanSegments(string? extractedText)
    {
        var normalized = NormalizeVisibleText(extractedText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var segments = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawSegment in normalized.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = NormalizeVisibleText(rawSegment);
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            segment = MessagePreviewPrefixPattern.Replace(segment, string.Empty);
            segment = MicrosoftTeamsSuffixPattern.Replace(segment, string.Empty);
            segment = NormalizeVisibleText(segment);

            if (string.IsNullOrWhiteSpace(segment)
                || MicrosoftTeamsWebContentPattern.IsMatch(segment)
                || BoilerplateSegmentPattern.IsMatch(segment)
                || DigitsOnlyPattern.IsMatch(segment))
            {
                continue;
            }

            if (seen.Add(segment))
            {
                segments.Add(segment);
            }
        }

        return segments.ToArray();
    }

    private static string[] CollapseDuplicatedOverlapTriplets(IReadOnlyList<string> segments)
    {
        if (segments.Count != 3)
        {
            return segments.ToArray();
        }

        for (var combinedIndex = 0; combinedIndex < segments.Count; combinedIndex++)
        {
            for (var messageIndex = 0; messageIndex < segments.Count; messageIndex++)
            {
                if (messageIndex == combinedIndex)
                {
                    continue;
                }

                for (var chatIndex = 0; chatIndex < segments.Count; chatIndex++)
                {
                    if (chatIndex == combinedIndex || chatIndex == messageIndex)
                    {
                        continue;
                    }

                    if (TryCollapseCombinedSegment(
                        segments[combinedIndex],
                        segments[messageIndex],
                        segments[chatIndex],
                        out var collapsed))
                    {
                        return collapsed;
                    }
                }
            }
        }

        return segments.ToArray();
    }

    private static string[] NormalizeTwoSegmentSenderFirst(IReadOnlyList<string> segments)
    {
        if (segments.Count != 2)
        {
            return segments.ToArray();
        }

        var firstSegment = segments[0];
        var secondSegment = segments[1];
        if (LooksLikeSenderMessage(firstSegment) || LooksLikeSenderMessage(secondSegment))
        {
            return segments.ToArray();
        }

        var firstLooksLikePersonName = LooksLikeSenderNameCandidate(firstSegment);
        var secondLooksLikePersonName = LooksLikeSenderNameCandidate(secondSegment);
        var firstLooksLikeTitle = LooksLikeTitleCandidate(firstSegment);
        var secondLooksLikeTitle = LooksLikeTitleCandidate(secondSegment);
        if (!firstLooksLikePersonName
            && secondLooksLikePersonName
            && !firstLooksLikeTitle
            && !secondLooksLikeTitle)
        {
            return new[] { secondSegment, firstSegment };
        }

        if (firstLooksLikePersonName
            && !secondLooksLikePersonName
            && !firstLooksLikeTitle
            && !secondLooksLikeTitle)
        {
            return new[] { firstSegment, secondSegment };
        }

        return segments.ToArray();
    }

    private static bool LooksLikeSenderNameCandidate(string segment)
    {
        return PersonNamePattern.IsMatch(segment)
            && !GetIdentityTokens(segment).Any(token => TitleKeywords.Contains(token) || NonPersonIdentityKeywords.Contains(token));
    }

    private static bool LooksLikeTitleCandidate(string segment)
    {
        return GetIdentityTokens(segment).Any(TitleKeywords.Contains);
    }

    private static bool TryCollapseCombinedSegment(
        string combinedSegment,
        string firstSegment,
        string secondSegment,
        out string[] collapsedSegments)
    {
        collapsedSegments = [];

        if (!string.Equals(
                combinedSegment,
                $"{firstSegment} {secondSegment}",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        collapsedSegments = OrderCollapsedSegments(firstSegment, secondSegment);
        return true;
    }

    private static string[] OrderCollapsedSegments(string firstSegment, string secondSegment)
    {
        if (LooksLikeSenderMessage(firstSegment) && !LooksLikeSenderMessage(secondSegment))
        {
            return new[] { firstSegment, secondSegment };
        }

        if (LooksLikeSenderMessage(secondSegment) && !LooksLikeSenderMessage(firstSegment))
        {
            return new[] { secondSegment, firstSegment };
        }

        var firstLooksLikeIdentityOrTitle = LooksLikeIdentityOrTitle(firstSegment);
        var secondLooksLikeIdentityOrTitle = LooksLikeIdentityOrTitle(secondSegment);

        if (firstLooksLikeIdentityOrTitle && !secondLooksLikeIdentityOrTitle)
        {
            return new[] { firstSegment, secondSegment };
        }

        if (secondLooksLikeIdentityOrTitle && !firstLooksLikeIdentityOrTitle)
        {
            return new[] { secondSegment, firstSegment };
        }

        return new[] { firstSegment, secondSegment };
    }

    private static bool LooksLikeSenderMessage(string segment)
    {
        return SenderMessagePattern.IsMatch(segment);
    }

    private static bool LooksLikeIdentityOrTitle(string segment)
    {
        return LooksLikePersonName(segment) || LooksLikeTitle(segment);
    }

    private static bool LooksLikePersonName(string segment)
    {
        var tokens = GetIdentityTokens(segment);
        if (tokens.Length is < 2 or > 4)
        {
            return false;
        }

        return tokens.All(token => char.IsUpper(token[0]) && !TitleKeywords.Contains(token));
    }

    private static bool LooksLikeTitle(string segment)
    {
        var tokens = GetIdentityTokens(segment);
        if (tokens.Length < 2 || tokens.Length > 8)
        {
            return false;
        }

        var hasConnectorWord = false;
        foreach (var token in tokens)
        {
            if (TitleConnectorWords.Contains(token))
            {
                hasConnectorWord = true;
                continue;
            }

            if (TitleKeywords.Contains(token))
            {
                return true;
            }

            if (!char.IsUpper(token[0]) && !char.IsDigit(token[0]))
            {
                return false;
            }
        }

        return hasConnectorWord || tokens.Length >= 3;
    }

    private static string[] GetIdentityTokens(string segment)
    {
        return IdentityTokenPattern.Matches(segment)
            .Select(match => match.Value)
            .ToArray();
    }

    private static string JoinSegments(IReadOnlyList<string> segments)
    {
        return segments.Count switch
        {
            0 => string.Empty,
            > 3 => string.Join(" | ", segments.Take(3)),
            _ => string.Join(" | ", segments)
        };
    }

    private static ParsedNotification? ParseNotification(IReadOnlyList<string> segmentsWithoutTestNotification)
    {
        if (segmentsWithoutTestNotification.Count == 0)
        {
            return null;
        }

        if (TryParseConversationMessage(segmentsWithoutTestNotification, out var conversationNotification))
        {
            return conversationNotification;
        }

        if (TryParseDirectMessage(segmentsWithoutTestNotification, out var directNotification))
        {
            return directNotification;
        }

        return new ParsedNotification(
            Sender: null,
            MessageBody: null,
            ConversationTitle: null,
            MessageType: ParsedNotificationMessageType.Unknown);
    }

    private static bool TryParseConversationMessage(
        IReadOnlyList<string> segments,
        out ParsedNotification notification)
    {
        notification = null!;
        if (segments.Count != 2)
        {
            return false;
        }

        string senderMessageSegment;
        string conversationTitleSegment;
        if (LooksLikeSenderMessage(segments[0]) && LooksLikeConversationTitleCandidate(segments[1]))
        {
            senderMessageSegment = segments[0];
            conversationTitleSegment = segments[1];
        }
        else if (LooksLikeSenderMessage(segments[1]) && LooksLikeConversationTitleCandidate(segments[0]))
        {
            senderMessageSegment = segments[1];
            conversationTitleSegment = segments[0];
        }
        else
        {
            return false;
        }

        if (!TrySplitSenderMessage(senderMessageSegment, out var sender, out var messageBody))
        {
            return false;
        }

        notification = new ParsedNotification(
            sender,
            messageBody,
            conversationTitleSegment,
            ParsedNotificationMessageType.ConversationMessage);
        return true;
    }

    private static bool TryParseDirectMessage(
        IReadOnlyList<string> segments,
        out ParsedNotification notification)
    {
        notification = null!;
        if (segments.Count != 2)
        {
            return false;
        }

        if (LooksLikeSenderMessage(segments[0]) || LooksLikeSenderMessage(segments[1]))
        {
            return false;
        }

        var firstLooksLikeSender = LooksLikeSenderNameCandidate(segments[0]);
        var secondLooksLikeSender = LooksLikeSenderNameCandidate(segments[1]);
        var firstLooksLikeTitle = LooksLikeConversationTitleCandidate(segments[0]);
        var secondLooksLikeTitle = LooksLikeConversationTitleCandidate(segments[1]);

        if (firstLooksLikeSender && !secondLooksLikeSender && !secondLooksLikeTitle)
        {
            notification = new ParsedNotification(
                segments[0],
                segments[1],
                null,
                ParsedNotificationMessageType.DirectMessage);
            return true;
        }

        if (secondLooksLikeSender && !firstLooksLikeSender && !firstLooksLikeTitle)
        {
            notification = new ParsedNotification(
                segments[1],
                segments[0],
                null,
                ParsedNotificationMessageType.DirectMessage);
            return true;
        }

        return false;
    }

    private static bool TrySplitSenderMessage(string segment, out string sender, out string messageBody)
    {
        sender = string.Empty;
        messageBody = string.Empty;

        var colonIndex = segment.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= segment.Length - 1)
        {
            return false;
        }

        sender = NormalizeVisibleText(segment[..colonIndex]);
        messageBody = NormalizeVisibleText(segment[(colonIndex + 1)..]);
        return !string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(messageBody);
    }

    private static bool LooksLikeConversationTitleCandidate(string segment)
    {
        return LooksLikeTitleCandidate(segment);
    }

    public static bool IsSuppressedContent(string cleanedMessage)
    {
        if (string.IsNullOrWhiteSpace(cleanedMessage))
        {
            return false;
        }

        if (string.Equals(cleanedMessage, "Thumbnail Preview", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cleanedMessage.Contains("Names recorded", StringComparison.OrdinalIgnoreCase)
            || cleanedMessage.Contains("Results shared", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cleanedMessage.Contains("marked as read", StringComparison.OrdinalIgnoreCase)
            || cleanedMessage.Contains("moved to", StringComparison.OrdinalIgnoreCase)
            || DeletedWordPattern.IsMatch(cleanedMessage)
            || FlaggedWordPattern.IsMatch(cleanedMessage))
        {
            return true;
        }

        return PollResultPattern.IsMatch(cleanedMessage);
    }

    private static bool LooksLikeRealMessageBannerCore(
        IReadOnlyList<string> segments,
        string cleanedWithoutTestNotification,
        bool containsTestNotificationSegment)
    {
        if (string.IsNullOrWhiteSpace(cleanedWithoutTestNotification)
            || containsTestNotificationSegment && segments.Count == 0
            || IsSuppressedContent(cleanedWithoutTestNotification))
        {
            return false;
        }

        if (segments[0].IndexOf(':') >= 0)
        {
            return true;
        }

        return segments.Count is 2 or 3;
    }
}

public sealed record TeamsNotificationTextAnalysis(
    string CleanedText,
    string CleanedWithoutTestNotification,
    IReadOnlyList<string> Segments,
    IReadOnlyList<string> SegmentsWithoutTestNotification,
    bool ContainsTestNotificationSegment,
    bool IsExactTestNotification,
    bool ContainsSuppressedContent,
    bool LooksLikeRealMessageBanner,
    ParsedNotification? ParsedNotification);

public sealed record ParsedNotification(
    string? Sender,
    string? MessageBody,
    string? ConversationTitle,
    ParsedNotificationMessageType MessageType);

public enum ParsedNotificationMessageType
{
    DirectMessage,
    ConversationMessage,
    Unknown
}

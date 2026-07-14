using TeamsRelay.Core;
using TeamsRelay.Source.TeamsUiAutomation;
using static TeamsRelay.Tests.TestHelpers;

namespace TeamsRelay.Tests;

public sealed class TeamsNotificationCaptureClassifierTests
{
    [Fact]
    public void WindowOpenedWithTextCreatesPendingCandidate()
    {
        var classifier = new TeamsNotificationCaptureClassifier();
        var openedAtUtc = new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero);

        var ready = classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc,
            rawEventKind: "window_opened",
            extractedText: "Message preview | Alex | Build complete"));

        // window_opened always goes to pending to give structure_changed a chance to enrich
        Assert.Empty(ready);

        var flushed = classifier.FlushExpired(openedAtUtc.AddSeconds(2));
        var candidate = Assert.Single(flushed);
        Assert.Equal("window_opened", candidate.CapturePath);
        Assert.Equal("window_opened", candidate.EventKind);
        Assert.Contains("Build complete", candidate.ExtractedText);
    }

    [Fact]
    public void RejectsOrphanStructureChangedFromMainWindow()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Message preview | Alex | Build complete",
            automationId: "chat-pane",
            topLevelWindowName: "Microsoft Teams"));

        Assert.Empty(ready);
    }

    [Fact]
    public void StructureChangedWithinWindowEnrichesPendingCandidate()
    {
        var classifier = new TeamsNotificationCaptureClassifier();
        var openedAtUtc = new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero);

        Assert.Empty(classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc,
            rawEventKind: "window_opened",
            extractedText: string.Empty)));

        var ready = classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc.AddMilliseconds(400),
            rawEventKind: "structure_changed",
            extractedText: "Message preview | Alex | Build complete",
            left: 110,
            top: 110,
            width: 320,
            height: 120));

        var candidate = Assert.Single(ready);
        Assert.Equal("window_opened_enriched", candidate.CapturePath);
        Assert.Contains("Build complete", candidate.ExtractedText);
    }

    [Fact]
    public void ExpiredStructureChangedDoesNotEnrichPendingCandidate()
    {
        var classifier = new TeamsNotificationCaptureClassifier();
        var openedAtUtc = new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero);

        Assert.Empty(classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc,
            rawEventKind: "window_opened",
            extractedText: string.Empty)));

        var ready = classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc.AddMilliseconds(1600),
            rawEventKind: "structure_changed",
            extractedText: "Message preview | Alex | Build complete"));

        Assert.Equal(2, ready.Count);
        Assert.Contains(ready, candidate => candidate.CapturePath == "window_opened");
        Assert.Contains(ready, candidate => candidate.CapturePath == "structure_changed_banner");
        Assert.DoesNotContain(ready, candidate => candidate.CapturePath == "window_opened_enriched");
    }

    [Fact]
    public void NonOverlappingStructureChangedDoesNotEnrichPendingCandidate()
    {
        var classifier = new TeamsNotificationCaptureClassifier();
        var openedAtUtc = new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero);

        Assert.Empty(classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc,
            rawEventKind: "window_opened",
            extractedText: string.Empty)));

        var ready = classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc.AddMilliseconds(400),
            rawEventKind: "structure_changed",
            extractedText: "Message preview | Alex | Build complete",
            left: 900,
            top: 900));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
    }

    [Fact]
    public void AllowsStandaloneStructureChangedCandidate()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Message preview | Alex | Build complete"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
        Assert.Equal("structure_changed", candidate.EventKind);
    }

    [Fact]
    public void AllowsKnownTeamsTestNotification()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "This is a test notification"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_test", candidate.CapturePath);
        Assert.Equal("structure_changed", candidate.EventKind);
        Assert.Equal("This is a test notification", candidate.ExtractedText);
    }

    [Theory]
    [InlineData("This is a test notification Message preview.")]
    [InlineData("Message preview. This is a test notification")]
    [InlineData("This is a test notification | Message preview.")]
    public void TestNotificationDetectedWhenMessagePreviewInlinedInSegment(string extractedText)
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: extractedText));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_test", candidate.CapturePath);
        Assert.Equal("This is a test notification", candidate.ExtractedText);
    }

    [Fact]
    public void TestNotificationWithMixedTextStaysIsolated()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "This is a test notification | Jordan: Can you review this? | Project Chat"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_test", candidate.CapturePath);
        Assert.Equal("This is a test notification", candidate.ExtractedText);
    }

    [Fact]
    public void TestNotificationDoesNotEnrichPendingCandidate()
    {
        var classifier = new TeamsNotificationCaptureClassifier();
        var openedAtUtc = new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero);

        Assert.Empty(classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc,
            rawEventKind: "window_opened",
            extractedText: string.Empty)));

        var ready = classifier.Process(CreateRecord(
            timestampUtc: openedAtUtc.AddMilliseconds(250),
            rawEventKind: "structure_changed",
            extractedText: "This is a test notification | Jordan: Can you review this? | Project Chat"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_test", candidate.CapturePath);
        Assert.Equal("This is a test notification", candidate.ExtractedText);
    }

    [Fact]
    public void AllowsStandaloneBannerWhenNotificationMetadataMatches()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Jordan: Can you review this? | Project Chat"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
    }

    [Fact]
    public void RejectsStandaloneBannerFromMainTeamsWindow()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Jordan: Can you review this? | Project Chat",
            automationId: "chat-pane",
            topLevelWindowName: "Microsoft Teams"));

        Assert.Empty(ready);
    }

    [Fact]
    public void AcceptsStandaloneBannerFromExactTeamsOverlayWindow()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Microsoft Teams | Microsoft Teams - Web content - Profile 2 | Untitled | Casey | can you review the release notes when you have a moment",
            automationId: string.Empty,
            topLevelWindowName: "Microsoft Teams"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
    }

    [Fact]
    public void AcceptsStandaloneBannerFromNotificationToast()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Jordan: Can you review this? | Project Chat",
            automationId: "toast-root",
            topLevelWindowName: "Microsoft Teams notification"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
    }

    [Fact]
    public void AcceptsStandaloneBannerWhenAutomationIdStartsWithToast()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Jordan: Can you review this? | Project Chat",
            automationId: "toast-container",
            topLevelWindowName: "Microsoft Teams"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
    }

    [Fact]
    public void RejectsStandaloneBannerFromMainTeamsWindowWithTitle()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Jordan: Can you review this? | Project Chat",
            automationId: "other-root",
            topLevelWindowName: "Microsoft Teams - Chat"));

        Assert.Empty(ready);
    }

    [Fact]
    public void RejectsStandaloneBannerFromPaneTitleMainTeamsWindow()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Taylor What are we doing with the release checklist? | What are we doing with the release checklist?",
            automationId: "message-body-1772901936409",
            topLevelWindowName: "Chat | Project Chat | Microsoft Teams"));

        Assert.Empty(ready);
    }

    [Fact]
    public void AcceptsStandaloneBannerFromUnknownOverlayWindow()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Jordan: Can you review this? | Project Chat",
            automationId: "other-root",
            topLevelWindowName: "Teams Call"));

        var candidate = Assert.Single(ready);
        Assert.Equal("structure_changed_banner", candidate.CapturePath);
    }

    [Fact]
    public void WindowOpenedFromMainTeamsWindowIsDropped()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "window_opened",
            extractedText: "Message preview | Alex | Build complete",
            topLevelWindowName: "Microsoft Teams",
            automationId: "chat-pane"));

        Assert.Empty(ready);
    }

    [Fact]
    public void WindowOpenedFromMainTeamsWindowWithTitleIsDropped()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "window_opened",
            extractedText: "Hey are you available?",
            topLevelWindowName: "Microsoft Teams - Chat",
            automationId: "chat-pane"));

        Assert.Empty(ready);
    }

    [Fact]
    public void DropsStandaloneSuppressedArtifactWithoutNotificationMetadata()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Poll: Choices recorded ; Results shared | Weekly Sync | Casey",
            automationId: "other-root",
            topLevelWindowName: "Microsoft Teams"));

        Assert.Empty(ready);
    }

    [Fact]
    public void DropsStandaloneStructureChangedWhenNotMessageLike()
    {
        var classifier = new TeamsNotificationCaptureClassifier();

        var ready = classifier.Process(CreateRecord(
            timestampUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            rawEventKind: "structure_changed",
            extractedText: "Microsoft Teams | Profile 1 | 2",
            automationId: "other-root",
            topLevelWindowName: "Microsoft Teams"));

        Assert.Empty(ready);
    }
}

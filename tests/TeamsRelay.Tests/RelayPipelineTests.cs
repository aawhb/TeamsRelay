using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class RelayPipelineTests
{
    [Fact]
    public void CreateDispatchExtractsAndFingerprintsMessage()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));
        var record = TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete");

        var created = pipeline.TryCreateDispatch(record, out var dispatch, out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Contains("Build complete", dispatch.Message);
        Assert.False(string.IsNullOrWhiteSpace(dispatch.Fingerprint));
    }

    [Fact]
    public void GenericPingModeDropsMessageContent()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Mode = "generic_ping",
                GenericPingText = "Ping",
                MaxMessageLength = 220
            }
        });
        var pipeline = new RelayPipeline(config, new FakeProcessNameResolver("ms-teams"));
        var record = TestHelpers.CreateTeamsRecord("Message preview | Secret text");

        var created = pipeline.TryCreateDispatch(record, out var dispatch, out _);

        Assert.True(created);
        Assert.Equal("Ping", dispatch.Message);
        Assert.False(dispatch.HasContent);
        Assert.True(dispatch.FallbackUsed);
    }

    [Fact]
    public void RejectsEventKindsOutsideAllowedSet()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));
        var record = TestHelpers.CreateTeamsRecord("Message preview | Should not pass", capturePath: string.Empty);

        var created = pipeline.TryCreateDispatch(record, out _, out var reason);

        Assert.False(created);
        Assert.Equal("not_banner_candidate", reason);
    }

    [Fact]
    public void RejectsRecordsOutsideStrictBounds()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));
        var record = TestHelpers.CreateTeamsRecord("Message preview | Should not pass", width: 1200);

        var created = pipeline.TryCreateDispatch(record, out _, out var reason);

        Assert.False(created);
        Assert.Equal("not_banner_candidate", reason);
    }

    [Fact]
    public void FlushReturnsCoalescedDispatch()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));
        pipeline.Add(TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete"));

        var flushed = pipeline.Flush(force: true);

        Assert.Single(flushed);
        Assert.Contains("Build complete", flushed[0].Message);
    }

    [Fact]
    public void FlushAllowsRepeatedDispatchesAcrossFlushes()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));
        var record = TestHelpers.CreateTeamsRecord("Message preview | Alex | Build complete");

        pipeline.Add(record);
        var firstFlush = pipeline.Flush(force: true);
        pipeline.Add(record);
        var secondFlush = pipeline.Flush(force: true);

        Assert.Single(firstFlush);
        Assert.Single(secondFlush);
    }

    [Theory]
    [InlineData("Thumbnail Preview", "thumbnail preview artifacts")]
    [InlineData("Poll: Choices recorded ; Results shared | Weekly Sync | Casey", "poll status artifacts")]
    [InlineData("Casey | 15% (2) | Taylor", "vote tally artifacts")]
    public void RejectsSuppressedContentArtifacts(string extractedText, string scenario)
    {
        _ = scenario;
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(extractedText),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("suppressed_content", reason);
    }

    [Fact]
    public void FlushPrefersEnrichedWindowOpenedCandidate()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        pipeline.Add(TestHelpers.CreateTeamsRecord(
            extractedText: "Message preview | Alex | Build complete",
            eventKind: "structure_changed",
            capturePath: "structure_changed_banner"));
        pipeline.Add(TestHelpers.CreateTeamsRecord(
            extractedText: "Message preview | Alex | Build complete",
            eventKind: "structure_changed",
            capturePath: "window_opened_enriched"));

        var flushed = pipeline.Flush(force: true);

        var dispatch = Assert.Single(flushed);
        Assert.Equal("window_opened_enriched", dispatch.CapturePath);
    }

    [Fact]
    public void AcceptsStrictModeTeamsTestNotificationCapturePath()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "This is a test notification",
                eventKind: "structure_changed",
                capturePath: "structure_changed_test",
                topLevelWindowName: "Microsoft Teams",
                automationId: "app-root"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal("structure_changed_test", dispatch.CapturePath);
        Assert.Equal("This is a test notification", dispatch.Message);
    }

    [Fact]
    public void TestCapturePathStripsMixedMessageContent()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "This is a test notification | Jordan: Can you review this? | Project Chat",
                eventKind: "structure_changed",
                capturePath: "structure_changed_test"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal("This is a test notification", dispatch.Message);
    }

    [Fact]
    public void NonTestCaptureStripsTestNotificationSegment()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "This is a test notification | Jordan: Can you review this? | Project Chat",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal("Jordan: Can you review this? | Project Chat", dispatch.Message);
    }

    [Fact]
    public void AcceptsStandaloneMessageBannerCapturePath()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Jordan: Can you review this? | Project Chat",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal("structure_changed_banner", dispatch.CapturePath);
    }

    [Fact]
    public void FiltersUnknownNotificationsWhenDisabled()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Filter = new DeliveryFilterOptions
                {
                    DirectMessages = true,
                    ConversationMessages = true,
                    UnknownTypes = false
                }
            }
        });
        var pipeline = new RelayPipeline(config, new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord("Viva Insights | Ready to focus?"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("filtered_by_type:Unknown", reason);
    }

    [Fact]
    public void GenericPingModeStillAppliesFiltering()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Mode = "generic_ping",
                GenericPingText = "Ping",
                Filter = new DeliveryFilterOptions
                {
                    DirectMessages = false,
                    ConversationMessages = true,
                    UnknownTypes = true
                }
            }
        });
        var pipeline = new RelayPipeline(config, new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord("Hey | Casey Morgan", eventKind: "structure_changed", capturePath: "structure_changed_banner"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("filtered_by_type:DirectMessage", reason);
    }

    [Fact]
    public void FiltersConversationMessagesWhenDisabled()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Filter = new DeliveryFilterOptions
                {
                    DirectMessages = true,
                    ConversationMessages = false,
                    UnknownTypes = true
                }
            }
        });
        var pipeline = new RelayPipeline(config, new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                "Jordan: Can you review this? | Project Chat",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("filtered_by_type:ConversationMessage", reason);
    }

    [Fact]
    public void AllFiltersEnabledPassesAllTypes()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Filter = new DeliveryFilterOptions
                {
                    DirectMessages = true,
                    ConversationMessages = true,
                    UnknownTypes = true
                }
            }
        });
        var pipeline = new RelayPipeline(config, new FakeProcessNameResolver("ms-teams"));

        var createdDM = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord("Hey | Casey Morgan", eventKind: "structure_changed", capturePath: "structure_changed_banner"),
            out _, out var reasonDM);
        var createdConvo = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord("Jordan: Can you review this? | Project Chat", eventKind: "structure_changed", capturePath: "structure_changed_banner"),
            out _, out var reasonConvo);
        var createdUnknown = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord("Viva Insights | Ready to focus?", eventKind: "structure_changed", capturePath: "structure_changed_banner"),
            out _, out var reasonUnknown);

        Assert.True(createdDM);
        Assert.Equal(string.Empty, reasonDM);
        Assert.True(createdConvo);
        Assert.Equal(string.Empty, reasonConvo);
        Assert.True(createdUnknown);
        Assert.Equal(string.Empty, reasonUnknown);
    }

    [Fact]
    public void UsesPipelineResolvedTextForFallbackFormatting()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "This is a test notification | Viva Insights | Ready to focus?",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal("Viva Insights | Ready to focus?", dispatch.Message);
    }

    [Fact]
    public void DefaultTemplatesPreserveCurrentOutputForExistingFixture()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Release Review | Casey started the meeting",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal("Release Review | Casey started the meeting", dispatch.Message);
    }

    [Fact]
    public void FingerprintIsStableAcrossDifferentUserTemplates()
    {
        var configA = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Format = new DeliveryFormatOptions
                {
                    DirectMessageTemplate = "{sender} | {message}",
                    ConversationMessageTemplate = "{sender}: {message} | {conversationTitle}",
                    FallbackTemplate = "{text}"
                }
            }
        });
        var configB = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Format = new DeliveryFormatOptions
                {
                    DirectMessageTemplate = "DM from {sender}: {message}",
                    ConversationMessageTemplate = "[{conversationTitle}] {sender}: {message}",
                    FallbackTemplate = "Teams => {text}"
                }
            }
        });

        var record = TestHelpers.CreateTeamsRecord(
            extractedText: "Jordan: Can you review this? | Project Chat",
            eventKind: "structure_changed",
            capturePath: "structure_changed_banner");

        var createdA = new RelayPipeline(configA, new FakeProcessNameResolver("ms-teams"))
            .TryCreateDispatch(record, out var dispatchA, out var reasonA);
        var createdB = new RelayPipeline(configB, new FakeProcessNameResolver("ms-teams"))
            .TryCreateDispatch(record, out var dispatchB, out var reasonB);

        Assert.True(createdA);
        Assert.True(createdB);
        Assert.Equal(string.Empty, reasonA);
        Assert.Equal(string.Empty, reasonB);
        Assert.NotEqual(dispatchA.Message, dispatchB.Message);
        Assert.Equal(dispatchA.Fingerprint, dispatchB.Fingerprint);
    }

    [Theory]
    [InlineData(
        "Jordan: Can you review this? Project Chat | Project Chat | Jordan: Can you review this?",
        "Jordan: Can you review this? | Project Chat",
        "duplicated group chat segments")]
    [InlineData(
        "Hey Casey Morgan | Casey Morgan | Hey",
        "Casey Morgan | Hey",
        "duplicated direct message segments")]
    [InlineData(
        "Release Review Casey started the meeting | Release Review | Casey started the meeting",
        "Release Review | Casey started the meeting",
        "duplicated title and content segments")]
    [InlineData(
        "I'm actually now free at closer to 4:00 your time if that's okay. | Alice Yu",
        "Alice Yu | I'm actually now free at closer to 4:00 your time if that's okay.",
        "two-segment sender message reordering")]
    [InlineData(
        "Release Review | Casey started the meeting",
        "Release Review | Casey started the meeting",
        "two-segment title and content left in place")]
    public void NormalizesNotificationText(string inputText, string expectedMessage, string scenario)
    {
        _ = scenario;
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: inputText,
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(expectedMessage, dispatch.Message);
    }

    [Fact]
    public void RejectsWebView2EventFromOutlookWindow()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("msedgewebview2"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Message preview | Alex | Build complete",
                topLevelWindowName: "Microsoft Outlook"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("not_teams_window", reason);
    }

    [Fact]
    public void AcceptsWebView2EventFromTeamsNotificationWindow()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("msedgewebview2"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Message preview | Alex | Build complete",
                topLevelWindowName: "Microsoft Teams notification"),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void RejectsEmptyTextWindowOpenedFromMainTeamsWindow()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: string.Empty,
                topLevelWindowName: "Microsoft Teams",
                automationId: "chat-pane"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("main_window_event", reason);
    }

    [Fact]
    public void RejectsTextEventFromMainTeamsWindow()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Message preview | Alex | Build complete",
                topLevelWindowName: "Microsoft Teams",
                automationId: "chat-pane"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("main_window_event", reason);
    }

    [Fact]
    public void AcceptsTextEventFromExactTeamsOverlayWindow()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Microsoft Teams | Microsoft Teams - Web content - Profile 2 | Untitled | Casey | can you review the release notes when you have a moment",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner",
                topLevelWindowName: "Microsoft Teams",
                automationId: string.Empty),
            out var dispatch,
            out var reason);

        Assert.True(created);
        Assert.Equal(string.Empty, reason);
        Assert.Contains("can you review the release notes", dispatch.Message);
    }

    [Fact]
    public void RejectsTextEventFromMainTeamsWindowWithChatTitle()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Hey are you available?",
                topLevelWindowName: "Microsoft Teams - Chat",
                automationId: "chat-pane"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("main_window_event", reason);
    }

    [Fact]
    public void RejectsTextEventFromPaneTitleMainTeamsWindow()
    {
        var pipeline = new RelayPipeline(RelayConfig.CreateDefault(), new FakeProcessNameResolver("msedgewebview2"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                extractedText: "Taylor What are we doing with the release checklist? | What are we doing with the release checklist?",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner",
                topLevelWindowName: "Chat | Project Chat | Microsoft Teams",
                automationId: "message-body-1772901936409"),
            out _,
            out var reason);

        Assert.False(created);
        Assert.Equal("main_window_event", reason);
    }

    [Fact]
    public void FullTextModeSetsHasContentFalseWhenFormatterFallsToGenericPing()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                GenericPingText = "Ping",
                Format = new DeliveryFormatOptions
                {
                    DirectMessageTemplate = null,
                    ConversationMessageTemplate = null,
                    Template = null,
                    FallbackTemplate = "{sender}"
                }
            }
        });
        var pipeline = new RelayPipeline(config, new FakeProcessNameResolver("ms-teams"));

        var created = pipeline.TryCreateDispatch(
            TestHelpers.CreateTeamsRecord(
                "Viva Insights | Ready to focus?",
                eventKind: "structure_changed",
                capturePath: "structure_changed_banner"),
            out var dispatch,
            out _);

        Assert.True(created);
        Assert.Equal("Ping", dispatch.Message);
        Assert.False(dispatch.HasContent);
        Assert.True(dispatch.FallbackUsed);
    }

}

using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class TeamsNotificationTextAnalyzerTests
{
    [Theory]
    [InlineData("marked as read")]
    [InlineData("Message marked as read")]
    [InlineData("deleted")]
    [InlineData("Item deleted")]
    [InlineData("flagged")]
    [InlineData("Message flagged")]
    [InlineData("moved to Deleted Items")]
    [InlineData("Conversation moved to Archive")]
    public void SuppressesOutlookActionPhrases(string text)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(text);

        Assert.True(result.ContainsSuppressedContent);
    }

    [Theory]
    [InlineData("Alex: Build complete")]
    [InlineData("Jordan: Can you review this?")]
    public void DoesNotSuppressRealMessages(string text)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(text);

        Assert.False(result.ContainsSuppressedContent);
    }

    [Theory]
    [InlineData(
        "Jordan: Can you review this? Project Chat | Project Chat | Jordan: Can you review this?",
        "Jordan: Can you review this? | Project Chat")]
    [InlineData(
        "Taylor: Sent an image Project Chat | Project Chat | Taylor: Sent an image",
        "Taylor: Sent an image | Project Chat")]
    [InlineData(
        "Hey Casey Morgan | Casey Morgan | Hey",
        "Casey Morgan | Hey")]
    [InlineData(
        "Can you review this tomorrow? Jordan Lee | Jordan Lee | Can you review this tomorrow?",
        "Jordan Lee | Can you review this tomorrow?")]
    [InlineData(
        "Release Review Casey started the meeting | Release Review | Casey started the meeting",
        "Release Review | Casey started the meeting")]
    public void CollapsesDuplicatedOverlapTriplets(string extractedText, string expected)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(extractedText);

        Assert.Equal(expected, result.CleanedText);
        Assert.Equal(expected, result.CleanedWithoutTestNotification);
        Assert.Equal(2, result.Segments.Count);
    }

    [Theory]
    [InlineData(
        "I'm actually now free at closer to 4:00 your time if that's okay. | Alice Yu",
        "Alice Yu | I'm actually now free at closer to 4:00 your time if that's okay.")]
    [InlineData(
        "Hey | Casey Morgan",
        "Casey Morgan | Hey")]
    public void ReordersTwoSegmentSenderMessages(string extractedText, string expected)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(extractedText);

        Assert.Equal(expected, result.CleanedText);
        Assert.Equal(expected, result.CleanedWithoutTestNotification);
        Assert.Equal(2, result.Segments.Count);
    }

    [Fact]
    public void LeavesTitleAndContentSegmentsInPlace()
    {
        var result = TeamsNotificationTextAnalyzer.Analyze("Release Review | Casey started the meeting");

        Assert.Equal("Release Review | Casey started the meeting", result.CleanedText);
        Assert.Equal("Release Review | Casey started the meeting", result.CleanedWithoutTestNotification);
    }

    [Fact]
    public void ParsesDirectMessageStructuredFields()
    {
        var result = TeamsNotificationTextAnalyzer.Analyze("Hey | Casey Morgan");

        Assert.NotNull(result.ParsedNotification);
        Assert.Equal(ParsedNotificationMessageType.DirectMessage, result.ParsedNotification!.MessageType);
        Assert.Equal("Casey Morgan", result.ParsedNotification.Sender);
        Assert.Equal("Hey", result.ParsedNotification.MessageBody);
        Assert.Null(result.ParsedNotification.ConversationTitle);
    }

    [Fact]
    public void ParsesConversationMessageStructuredFields()
    {
        var result = TeamsNotificationTextAnalyzer.Analyze("Jordan: Can you review this? | Project Chat");

        Assert.NotNull(result.ParsedNotification);
        Assert.Equal(ParsedNotificationMessageType.ConversationMessage, result.ParsedNotification!.MessageType);
        Assert.Equal("Jordan", result.ParsedNotification.Sender);
        Assert.Equal("Can you review this?", result.ParsedNotification.MessageBody);
        Assert.Equal("Project Chat", result.ParsedNotification.ConversationTitle);
    }

    [Theory]
    [InlineData("Viva Insights | Ready to focus?")]
    [InlineData("Updates | You have an update due soon")]
    [InlineData("SM Everyone - Communicate It | Atchara Phanpaktra posted an announcement")]
    [InlineData("Alex | Build complete")]
    public void ClassifiesEmbeddedAppsAndLowConfidenceCasesAsUnknown(string extractedText)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(extractedText);

        Assert.NotNull(result.ParsedNotification);
        Assert.Equal(ParsedNotificationMessageType.Unknown, result.ParsedNotification!.MessageType);
        Assert.Null(result.ParsedNotification.Sender);
        Assert.Null(result.ParsedNotification.MessageBody);
        Assert.Null(result.ParsedNotification.ConversationTitle);
    }

    [Theory]
    [InlineData("Casey started the meeting")]
    [InlineData("Build complete")]
    [InlineData("You have 3 unread messages")]
    public void SingleSegmentNotificationsClassifyAsUnknown(string extractedText)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(extractedText);

        Assert.NotNull(result.ParsedNotification);
        Assert.Equal(ParsedNotificationMessageType.Unknown, result.ParsedNotification!.MessageType);
        Assert.Null(result.ParsedNotification.Sender);
        Assert.Null(result.ParsedNotification.MessageBody);
        Assert.Null(result.ParsedNotification.ConversationTitle);
    }

    [Theory]
    [InlineData("Jordan: Can you review this? | Project Chat | Extra segment")]
    [InlineData("Casey Morgan | Hey there | Something else | And more")]
    public void ThreeOrMoreSegmentNotificationsClassifyAsUnknown(string extractedText)
    {
        var result = TeamsNotificationTextAnalyzer.Analyze(extractedText);

        Assert.NotNull(result.ParsedNotification);
        Assert.Equal(ParsedNotificationMessageType.Unknown, result.ParsedNotification!.MessageType);
        Assert.Null(result.ParsedNotification.Sender);
        Assert.Null(result.ParsedNotification.MessageBody);
        Assert.Null(result.ParsedNotification.ConversationTitle);
    }
}

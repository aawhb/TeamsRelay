using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class NotificationFormatterTests
{
    private readonly NotificationFormatter _formatter = new();

    [Fact]
    public void UsesTypeSpecificTemplateWhenAvailable()
    {
        var result = _formatter.Format(
            new ParsedNotification("Casey Morgan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Casey Morgan | Hey",
            new DeliveryFormatOptions(),
            "Ping");

        Assert.Equal("Casey Morgan | Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void FallsBackToResolvedTextWhenTypeSpecificTemplateHasMissingVariables()
    {
        var result = _formatter.Format(
            new ParsedNotification("Jordan", "Can you review this?", null, ParsedNotificationMessageType.ConversationMessage),
            "Jordan: Can you review this?",
            new DeliveryFormatOptions
            {
                ConversationMessageTemplate = "{sender}: {message} | {conversationTitle}"
            },
            "Ping");

        Assert.Equal("Jordan: Can you review this?", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void FallsBackToResolvedTextWhenTypeSpecificTemplateIsNull()
    {
        var result = _formatter.Format(
            new ParsedNotification("Casey Morgan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Casey Morgan | Hey",
            new DeliveryFormatOptions
            {
                DirectMessageTemplate = null
            },
            "Ping");

        Assert.Equal("Casey Morgan | Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void UsesResolvedTextForUnknownNotifications()
    {
        var result = _formatter.Format(
            new ParsedNotification(null, null, null, ParsedNotificationMessageType.Unknown),
            "Viva Insights | Ready to focus?",
            new DeliveryFormatOptions(),
            "Ping");

        Assert.Equal("Viva Insights | Ready to focus?", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void FallsBackToGenericPingWhenResolvedTextEmpty()
    {
        var result = _formatter.Format(
            new ParsedNotification(null, null, null, ParsedNotificationMessageType.Unknown),
            string.Empty,
            new DeliveryFormatOptions
            {
                DirectMessageTemplate = null,
                ConversationMessageTemplate = null
            },
            "Ping");

        Assert.Equal("Ping", result.Message);
        Assert.True(result.UsedGenericPing);
    }

    [Fact]
    public void CollapsesWhitespaceAfterRendering()
    {
        var result = _formatter.Format(
            new ParsedNotification("Jordan", "Can you review this?", "Project Chat", ParsedNotificationMessageType.ConversationMessage),
            "Jordan: Can you review this? | Project Chat",
            new DeliveryFormatOptions
            {
                ConversationMessageTemplate = "  {sender}:   {message}    |   {conversationTitle}  "
            },
            "Ping");

        Assert.Equal("Jordan: Can you review this? | Project Chat", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void TextVariableWorksInTypeSpecificTemplate()
    {
        var result = _formatter.Format(
            new ParsedNotification("Jordan", "Can you review this?", "Project Chat", ParsedNotificationMessageType.ConversationMessage),
            "Jordan: Can you review this? | Project Chat",
            new DeliveryFormatOptions
            {
                ConversationMessageTemplate = "Teams: {text}"
            },
            "Ping");

        Assert.Equal("Teams: Jordan: Can you review this? | Project Chat", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void UnknownVariablesAreLeftAsLiterals()
    {
        var result = _formatter.Format(
            new ParsedNotification("Jordan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Jordan | Hey",
            new DeliveryFormatOptions
            {
                DirectMessageTemplate = "{foo}: {sender} | {message}"
            },
            "Ping");

        Assert.Equal("{foo}: Jordan | Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void GenericPingUsedWhenResolvedTextAndTemplatesEmpty()
    {
        var result = _formatter.Format(
            null,
            string.Empty,
            new DeliveryFormatOptions
            {
                DirectMessageTemplate = null,
                ConversationMessageTemplate = null
            },
            "New Teams activity");

        Assert.Equal("New Teams activity", result.Message);
        Assert.True(result.UsedGenericPing);
    }
}

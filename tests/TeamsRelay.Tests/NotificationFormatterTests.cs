using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class NotificationFormatterTests
{
    private readonly NotificationFormatter formatter = new();

    [Fact]
    public void UsesTypeSpecificTemplateWhenAvailable()
    {
        var result = formatter.Format(
            new ParsedNotification("Casey Morgan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Casey Morgan | Hey",
            new DeliveryFormatOptions(),
            "Ping");

        Assert.Equal("Casey Morgan | Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void TypeSpecificTemplateOverridesGeneralTemplate()
    {
        var result = formatter.Format(
            new ParsedNotification("Casey Morgan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Casey Morgan | Hey",
            new DeliveryFormatOptions
            {
                Template = "General: {sender} says {message}",
                DirectMessageTemplate = "DM: {sender} | {message}",
                FallbackTemplate = "{text}"
            },
            "Ping");

        Assert.Equal("DM: Casey Morgan | Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void FallsBackToGeneralTemplateWhenTypeSpecificTemplateHasMissingVariables()
    {
        var result = formatter.Format(
            new ParsedNotification("Jordan", "Can you review this?", null, ParsedNotificationMessageType.ConversationMessage),
            "Jordan: Can you review this?",
            new DeliveryFormatOptions
            {
                Template = "{sender}: {message}",
                ConversationMessageTemplate = "{sender}: {message} | {conversationTitle}",
                FallbackTemplate = "{text}"
            },
            "Ping");

        Assert.Equal("Jordan: Can you review this?", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void FallsBackToGeneralTemplateWhenTypeSpecificTemplateIsNull()
    {
        var result = formatter.Format(
            new ParsedNotification("Casey Morgan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Casey Morgan | Hey",
            new DeliveryFormatOptions
            {
                Template = "From {sender}: {message}",
                DirectMessageTemplate = null,
                FallbackTemplate = "{text}"
            },
            "Ping");

        Assert.Equal("From Casey Morgan: Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void UsesFallbackTemplateTextForUnknownNotifications()
    {
        var result = formatter.Format(
            new ParsedNotification(null, null, null, ParsedNotificationMessageType.Unknown),
            "Viva Insights | Ready to focus?",
            new DeliveryFormatOptions(),
            "Ping");

        Assert.Equal("Viva Insights | Ready to focus?", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void FallsBackToGenericPingWhenFallbackTemplateRendersEmpty()
    {
        var result = formatter.Format(
            new ParsedNotification(null, null, null, ParsedNotificationMessageType.Unknown),
            string.Empty,
            new DeliveryFormatOptions
            {
                Template = null,
                DirectMessageTemplate = null,
                ConversationMessageTemplate = null,
                FallbackTemplate = "{message}"
            },
            "Ping");

        Assert.Equal("Ping", result.Message);
        Assert.True(result.UsedGenericPing);
    }

    [Fact]
    public void CollapsesWhitespaceAfterRendering()
    {
        var result = formatter.Format(
            new ParsedNotification("Jordan", "Can you review this?", "Project Chat", ParsedNotificationMessageType.ConversationMessage),
            "Jordan: Can you review this? | Project Chat",
            new DeliveryFormatOptions
            {
                ConversationMessageTemplate = "  {sender}:   {message}    |   {conversationTitle}  ",
                FallbackTemplate = "{text}"
            },
            "Ping");

        Assert.Equal("Jordan: Can you review this? | Project Chat", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void TextVariableWorksInNonFallbackTemplate()
    {
        var result = formatter.Format(
            new ParsedNotification("Jordan", "Can you review this?", "Project Chat", ParsedNotificationMessageType.ConversationMessage),
            "Jordan: Can you review this? | Project Chat",
            new DeliveryFormatOptions
            {
                ConversationMessageTemplate = "Teams: {text}",
                FallbackTemplate = "{text}"
            },
            "Ping");

        Assert.Equal("Teams: Jordan: Can you review this? | Project Chat", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void UnknownVariablesAreLeftAsLiterals()
    {
        var result = formatter.Format(
            new ParsedNotification("Jordan", "Hey", null, ParsedNotificationMessageType.DirectMessage),
            "Jordan | Hey",
            new DeliveryFormatOptions
            {
                DirectMessageTemplate = "{foo}: {sender} | {message}",
                FallbackTemplate = "{text}"
            },
            "Ping");

        Assert.Equal("{foo}: Jordan | Hey", result.Message);
        Assert.False(result.UsedGenericPing);
    }

    [Fact]
    public void GenericPingIndicatesUsedGenericPingTrue()
    {
        var result = formatter.Format(
            null,
            string.Empty,
            new DeliveryFormatOptions
            {
                Template = null,
                DirectMessageTemplate = null,
                ConversationMessageTemplate = null,
                FallbackTemplate = "{sender}"
            },
            "New Teams activity");

        Assert.Equal("New Teams activity", result.Message);
        Assert.True(result.UsedGenericPing);
    }
}

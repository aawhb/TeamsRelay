using System.Text.RegularExpressions;

namespace TeamsRelay.Core;

public sealed class NotificationFormatter
{
    private static readonly Regex TemplateVariablePattern = new(@"\{(?<name>sender|message|conversationTitle|text)\}", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public NotificationFormatResult Format(
        ParsedNotification? parsedNotification,
        string resolvedText,
        DeliveryFormatOptions format,
        string genericPingText)
    {
        if (TryRenderStrictTemplate(GetTypeSpecificTemplate(parsedNotification, format), parsedNotification, resolvedText, out var rendered))
        {
            return new NotificationFormatResult(rendered, UsedGenericPing: false);
        }

        if (TryRenderStrictTemplate(format.Template, parsedNotification, resolvedText, out rendered))
        {
            return new NotificationFormatResult(rendered, UsedGenericPing: false);
        }

        if (TryRenderFallbackTemplate(format.FallbackTemplate, parsedNotification, resolvedText, out rendered))
        {
            return new NotificationFormatResult(rendered, UsedGenericPing: false);
        }

        return new NotificationFormatResult(genericPingText, UsedGenericPing: true);
    }

    private static string? GetTypeSpecificTemplate(ParsedNotification? parsedNotification, DeliveryFormatOptions format)
    {
        return parsedNotification?.MessageType switch
        {
            ParsedNotificationMessageType.DirectMessage => format.DirectMessageTemplate,
            ParsedNotificationMessageType.ConversationMessage => format.ConversationMessageTemplate,
            _ => null
        };
    }

    private static bool TryRenderStrictTemplate(
        string? template,
        ParsedNotification? parsedNotification,
        string resolvedText,
        out string rendered)
    {
        rendered = string.Empty;

        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var missingVariable = false;
        rendered = TemplateVariablePattern.Replace(template, match =>
        {
            var value = ResolveVariable(match.Groups["name"].Value, parsedNotification, resolvedText);

            if (string.IsNullOrWhiteSpace(value))
            {
                missingVariable = true;
                return string.Empty;
            }

            return value;
        });

        rendered = NormalizeRenderedOutput(rendered);
        return !missingVariable && !string.IsNullOrWhiteSpace(rendered);
    }

    private static bool TryRenderFallbackTemplate(
        string template,
        ParsedNotification? parsedNotification,
        string resolvedText,
        out string rendered)
    {
        rendered = TemplateVariablePattern.Replace(template, match =>
        {
            return ResolveVariable(match.Groups["name"].Value, parsedNotification, resolvedText)
                ?? string.Empty;
        });

        rendered = NormalizeRenderedOutput(rendered);
        return !string.IsNullOrWhiteSpace(rendered);
    }

    private static string? ResolveVariable(string name, ParsedNotification? parsedNotification, string resolvedText)
    {
        return name switch
        {
            "sender" => parsedNotification?.Sender,
            "message" => parsedNotification?.MessageBody,
            "conversationTitle" => parsedNotification?.ConversationTitle,
            "text" => resolvedText,
            _ => null
        };
    }

    private static string NormalizeRenderedOutput(string value)
    {
        return WhitespacePattern.Replace(value, " ").Trim();
    }
}

public sealed record NotificationFormatResult(string Message, bool UsedGenericPing);

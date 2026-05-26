namespace TeamsRelay.Core;

public sealed class RelayConfig
{
    public TargetOptions Target { get; init; } = new();

    public DeliveryOptions Delivery { get; init; } = new();

    public RuntimeOptions Runtime { get; init; } = new();

    public static RelayConfig CreateDefault() => new();

    public static RelayConfig NormalizeAndValidate(RelayConfig? config)
    {
        if (config is null)
        {
            throw new CliException("Config file is empty or invalid.");
        }

        var errors = new List<string>();

        var kdeCliPath = Normalize(config.Target?.KdeCliPath, "kdeconnect-cli");
        if (string.IsNullOrWhiteSpace(kdeCliPath))
        {
            errors.Add("target.kdeCliPath: cannot be empty.");
        }

        var deviceIds = (config.Target?.DeviceIds ?? [])
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var deliveryMode = Normalize(config.Delivery?.Mode, "full_text");
        if (deliveryMode is not "full_text" and not "generic_ping")
        {
            errors.Add("delivery.mode: must be 'full_text' or 'generic_ping'.");
        }

        var genericPingText = Normalize(config.Delivery?.GenericPingText, "New Teams activity");
        if (string.IsNullOrWhiteSpace(genericPingText))
        {
            errors.Add("delivery.genericPingText: cannot be empty.");
        }

        var maxMessageLength = config.Delivery?.MaxMessageLength ?? 220;
        if (maxMessageLength is < 20 or > 2000)
        {
            errors.Add("delivery.maxMessageLength: must be between 20 and 2000.");
        }

        var filter = config.Delivery?.Filter;
        var deliveryFilter = new DeliveryFilterOptions
        {
            DirectMessages = filter?.DirectMessages ?? true,
            ConversationMessages = filter?.ConversationMessages ?? true,
            UnknownTypes = filter?.UnknownTypes ?? true
        };

        var format = config.Delivery?.Format;
        var directMessageTemplate = NormalizeOptional(format?.DirectMessageTemplate);
        var conversationMessageTemplate = NormalizeOptional(format?.ConversationMessageTemplate);

        var logLevel = Normalize(config.Runtime?.LogLevel, "info").ToLowerInvariant();
        if (logLevel is not "info" and not "debug")
        {
            errors.Add("runtime.logLevel: must be 'info' or 'debug'.");
        }

        if (errors.Count > 0)
        {
            throw new CliException("Config validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        return new RelayConfig
        {
            Target = new TargetOptions
            {
                KdeCliPath = kdeCliPath,
                DeviceIds = deviceIds
            },
            Delivery = new DeliveryOptions
            {
                Mode = deliveryMode,
                GenericPingText = genericPingText,
                MaxMessageLength = maxMessageLength,
                Filter = deliveryFilter,
                Format = format is null
                    ? new DeliveryFormatOptions()
                    : new DeliveryFormatOptions
                    {
                        DirectMessageTemplate = directMessageTemplate,
                        ConversationMessageTemplate = conversationMessageTemplate
                    }
            },
            Runtime = new RuntimeOptions
            {
                LogLevel = logLevel
            }
        };
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeOptional(string? value, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }
}

public sealed class TargetOptions
{
    public string KdeCliPath { get; init; } = "kdeconnect-cli";

    public string[] DeviceIds { get; init; } = [];
}

public sealed class DeliveryOptions
{
    public string Mode { get; init; } = "full_text";

    public string GenericPingText { get; init; } = "New Teams activity";

    public int MaxMessageLength { get; init; } = 220;

    public DeliveryFilterOptions Filter { get; init; } = new();

    public DeliveryFormatOptions Format { get; init; } = new();
}

public sealed class DeliveryFilterOptions
{
    public bool DirectMessages { get; init; } = true;

    public bool ConversationMessages { get; init; } = true;

    public bool UnknownTypes { get; init; } = true;
}

public sealed class DeliveryFormatOptions
{
    public string? DirectMessageTemplate { get; init; } = "{sender} | {message}";

    public string? ConversationMessageTemplate { get; init; } = "{sender}: {message} | {conversationTitle}";
}

public sealed class RuntimeOptions
{
    public string LogLevel { get; init; } = "info";
}

public sealed record ResolvedRelayConfig(string Path, RelayConfig Config);

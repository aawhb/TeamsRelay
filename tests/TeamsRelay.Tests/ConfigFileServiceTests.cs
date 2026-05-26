using System.Text.Json;
using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class ConfigFileServiceTests
{
    [Fact]
    public void DefaultConfigRoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(RelayConfig.CreateDefault(), JsonDefaults.Parsing);
        var parsed = JsonSerializer.Deserialize<RelayConfig>(json, JsonDefaults.Parsing);

        var validated = RelayConfig.NormalizeAndValidate(parsed);
        Assert.True(validated.Delivery.Filter.DirectMessages);
        Assert.True(validated.Delivery.Filter.ConversationMessages);
        Assert.True(validated.Delivery.Filter.UnknownTypes);
        Assert.Equal("{sender} | {message}", validated.Delivery.Format.DirectMessageTemplate);
        Assert.Equal("{sender}: {message} | {conversationTitle}", validated.Delivery.Format.ConversationMessageTemplate);
        Assert.DoesNotContain("dedupeWindowSeconds", json);
    }

    [Fact]
    public async Task LoadIgnoresUnknownProperties()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var service = new ConfigFileService(environment);
        var configPath = environment.DefaultConfigPath;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, """
        {
          "version": 1,
          "source": {
            "kind": "teams_uia"
          },
          "target": {
            "kind": "kde_connect",
            "kdeCliPath": "kdeconnect-cli",
            "deviceIds": []
          },
          "delivery": {
            "mode": "full_text",
            "genericPingText": "New Teams activity",
            "maxMessageLength": 220,
            "dedupeWindowSeconds": 8,
            "format": {
              "template": "ignored"
            }
          },
          "runtime": {
            "logLevel": "info"
          }
        }
        """);

        var resolved = await service.LoadAsync(path: null);

        Assert.Equal("full_text", resolved.Config.Delivery.Mode);
        Assert.Equal(220, resolved.Config.Delivery.MaxMessageLength);
    }

    [Fact]
    public void NormalizeAndValidateAcceptsNullTypeSpecificTemplates()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Delivery = new DeliveryOptions
            {
                Format = new DeliveryFormatOptions
                {
                    DirectMessageTemplate = null,
                    ConversationMessageTemplate = null
                }
            }
        });

        Assert.Null(config.Delivery.Format.DirectMessageTemplate);
        Assert.Null(config.Delivery.Format.ConversationMessageTemplate);
    }

}

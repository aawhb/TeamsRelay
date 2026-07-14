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
    }

    [Fact]
    public async Task LoadRejectsUnknownProperties()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var service = new ConfigFileService(environment);
        var configPath = environment.DefaultConfigPath;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, """
        {
          "runtime": {
            "logLevel": "info",
            "memorySnapshotIntervalSeconds": 60
          }
        }
        """);

        var exception = await Assert.ThrowsAsync<CliException>(() => service.LoadAsync(path: null));

        Assert.Contains("memorySnapshotIntervalSeconds", exception.Message);
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

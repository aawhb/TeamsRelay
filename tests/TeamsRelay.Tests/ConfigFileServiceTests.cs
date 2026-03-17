using System.Text.Json;
using TeamsRelay.Core;

namespace TeamsRelay.Tests;

public sealed class ConfigFileServiceTests
{
    [Fact]
    public async Task InitializeWritesDefaultConfig()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var service = new ConfigFileService(environment);

        var writtenPath = await service.InitializeAsync(path: null, force: false);

        Assert.Equal(Path.Combine(root, "config", "relay.config.json"), writtenPath);
        Assert.True(File.Exists(writtenPath));
        Assert.DoesNotContain("dedupeWindowSeconds", await File.ReadAllTextAsync(writtenPath));

        var parsed = JsonSerializer.Deserialize<RelayConfig>(await File.ReadAllTextAsync(writtenPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var validated = RelayConfig.NormalizeAndValidate(parsed);
        Assert.Equal(1, validated.Version);
        Assert.Equal("teams_uia", validated.Source.Kind);
        Assert.Equal("strict", validated.Source.CaptureMode);
        Assert.Equal("kde_connect", validated.Target.Kind);
        Assert.True(validated.Delivery.Filter.DirectMessages);
        Assert.True(validated.Delivery.Filter.ConversationMessages);
        Assert.True(validated.Delivery.Filter.UnknownTypes);
        Assert.Equal("{sender} | {message}", validated.Delivery.Format.DirectMessageTemplate);
        Assert.Equal("{sender}: {message} | {conversationTitle}", validated.Delivery.Format.ConversationMessageTemplate);
        Assert.Equal("{text}", validated.Delivery.Format.FallbackTemplate);
        Assert.Equal(0, validated.Runtime.MemorySnapshotIntervalSeconds);
        Assert.Equal("both", validated.Runtime.UiaSubscriptionMode);
    }

    [Fact]
    public async Task InitializeRequiresForceToOverwrite()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var service = new ConfigFileService(environment);

        await service.InitializeAsync(path: null, force: false);
        var exception = await Assert.ThrowsAsync<CliException>(() => service.InitializeAsync(path: null, force: false));

        Assert.Contains("Config already exists", exception.Message);
    }

    [Fact]
    public async Task LoadValidatesConfigVersion()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var service = new ConfigFileService(environment);
        var configPath = environment.DefaultConfigPath;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, """
        {
          "version": 2
        }
        """);

        var exception = await Assert.ThrowsAsync<CliException>(() => service.LoadAsync(path: null));

        Assert.Contains("version: must equal 1.", exception.Message);
    }

    [Fact]
    public async Task LoadIgnoresLegacyDedupeWindowSecondsProperty()
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
            "kind": "teams_uia",
            "captureMode": "strict"
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
            "dedupeWindowSeconds": 8
          },
          "runtime": {
            "logLevel": "info"
          }
        }
        """);

        var resolved = await service.LoadAsync(path: null);

        Assert.Equal(1, resolved.Config.Version);
        Assert.Equal("full_text", resolved.Config.Delivery.Mode);
        Assert.Equal(220, resolved.Config.Delivery.MaxMessageLength);
    }

    [Fact]
    public void NormalizeAndValidateRejectsInvalidCaptureMode()
    {
        var exception = Assert.Throws<CliException>(() => RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Source = new SourceOptions
            {
                Kind = "teams_uia",
                CaptureMode = "loose"
            }
        }));

        Assert.Contains("source.captureMode: must be 'strict' or 'hybrid'.", exception.Message);
    }

    [Fact]
    public void NormalizeAndValidateRejectsEmptyFallbackTemplate()
    {
        var exception = Assert.Throws<CliException>(() => RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Format = new DeliveryFormatOptions
                {
                    FallbackTemplate = "   "
                }
            }
        }));

        Assert.Contains("delivery.format.fallbackTemplate: cannot be empty.", exception.Message);
    }

    [Fact]
    public void NormalizeAndValidateAcceptsNullTypeSpecificTemplates()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Delivery = new DeliveryOptions
            {
                Format = new DeliveryFormatOptions
                {
                    DirectMessageTemplate = null,
                    ConversationMessageTemplate = null,
                    FallbackTemplate = "{text}"
                }
            }
        });

        Assert.Null(config.Delivery.Format.DirectMessageTemplate);
        Assert.Null(config.Delivery.Format.ConversationMessageTemplate);
        Assert.Equal("{text}", config.Delivery.Format.FallbackTemplate);
    }

    [Fact]
    public void NormalizeAndValidateRejectsNegativeMemorySnapshotInterval()
    {
        var exception = Assert.Throws<CliException>(() => RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Runtime = new RuntimeOptions
            {
                LogLevel = "info",
                MemorySnapshotIntervalSeconds = -1
            }
        }));

        Assert.Contains("runtime.memorySnapshotIntervalSeconds: must be greater than or equal to 0.", exception.Message);
    }

    [Fact]
    public void NormalizeAndValidateAcceptsPositiveMemorySnapshotInterval()
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Runtime = new RuntimeOptions
            {
                LogLevel = "info",
                MemorySnapshotIntervalSeconds = 60
            }
        });

        Assert.Equal(60, config.Runtime.MemorySnapshotIntervalSeconds);
    }

    [Fact]
    public async Task LoadRejectsNonNumericMemorySnapshotInterval()
    {
        var root = TestHelpers.CreateTemporaryDirectory();
        var environment = new AppEnvironment(root);
        var service = new ConfigFileService(environment);
        var configPath = environment.DefaultConfigPath;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, """
        {
          "version": 1,
          "runtime": {
            "logLevel": "info",
            "memorySnapshotIntervalSeconds": "sixty"
          }
        }
        """);

        var exception = await Assert.ThrowsAsync<CliException>(() => service.LoadAsync(path: null));

        Assert.Contains("Failed to parse config", exception.Message);
        Assert.Contains("memorySnapshotIntervalSeconds", exception.Message);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("window_opened_only")]
    [InlineData("structure_changed_only")]
    public void NormalizeAndValidateAcceptsValidUiaSubscriptionModes(string mode)
    {
        var config = RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Runtime = new RuntimeOptions
            {
                LogLevel = "info",
                UiaSubscriptionMode = mode
            }
        });

        Assert.Equal(mode, config.Runtime.UiaSubscriptionMode);
    }

    [Fact]
    public void NormalizeAndValidateRejectsInvalidUiaSubscriptionMode()
    {
        var exception = Assert.Throws<CliException>(() => RelayConfig.NormalizeAndValidate(new RelayConfig
        {
            Version = 1,
            Runtime = new RuntimeOptions
            {
                LogLevel = "info",
                UiaSubscriptionMode = "window_only"
            }
        }));

        Assert.Contains("runtime.uiaSubscriptionMode: must be 'both', 'window_opened_only', or 'structure_changed_only'.", exception.Message);
    }
}

using Microsoft.Extensions.Configuration;
using SubliminalCart3lBot.Services;
using Xunit;

namespace SubliminalCart3lBot.Tests;

public sealed class TelegramSettingsFactoryTests
{
    private const string ValidSecret = "test_secret_0123456789abcdef0123456789";

    [Fact]
    public void MissingBotTokenIsRejected()
    {
        var configuration = CreateConfiguration(botToken: null, webhookSecret: ValidSecret);

        var exception = Assert.Throws<InvalidOperationException>(
            () => TelegramSettingsFactory.Create(configuration));

        Assert.Contains("Telegram:BotToken", exception.Message);
    }

    [Fact]
    public void InvalidWebhookSecretIsRejected()
    {
        var configuration = CreateConfiguration(botToken: "local-test-not-a-real-token", webhookSecret: "bad secret");

        var exception = Assert.Throws<InvalidOperationException>(
            () => TelegramSettingsFactory.Create(configuration));

        Assert.Contains("Telegram:WebhookSecret", exception.Message);
    }

    private static IConfiguration CreateConfiguration(string? botToken, string webhookSecret)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = botToken,
                ["Telegram:WebhookSecret"] = webhookSecret,
                ["Telegram:PublicBaseUrl"] = "https://subliminalcart3l-001-site1.ctempurl.com/"
            })
            .Build();
}
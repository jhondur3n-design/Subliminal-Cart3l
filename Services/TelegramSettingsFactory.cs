using Microsoft.Extensions.Configuration;

namespace SubliminalCart3lBot.Services;

public static class TelegramSettingsFactory
{
    public const long OwnerTelegramId = 6197139693;

    public static TelegramSettings Create(IConfiguration configuration)
    {
        var telegramSection = configuration.GetSection("Telegram");
        var botToken = telegramSection["BotToken"]?.Trim() ?? "";
        var webhookSecret = telegramSection["WebhookSecret"]?.Trim() ?? "";
        var publicBaseUrl = telegramSection["PublicBaseUrl"]?.Trim().TrimEnd('/') ?? "";

        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new InvalidOperationException("Configure Telegram:BotToken using a secure application setting.");
        }

        if (webhookSecret.Length < 32
            || webhookSecret.Length > 256
            || webhookSecret.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new InvalidOperationException(
                "Telegram:WebhookSecret must contain 32-256 characters using only letters, numbers, '_' or '-'.");
        }

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri)
            || publicUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Telegram:PublicBaseUrl must be an absolute HTTPS URL.");
        }

        return new TelegramSettings(
            botToken,
            webhookSecret,
            publicBaseUrl,
            OwnerTelegramId,
            configuration["Store:SupportUsername"]?.Trim().TrimStart('@') ?? "",
            configuration["Store:Terms"]?.Trim()
                ?? "Digital WAV products are delivered through Telegram after successful Telegram Stars payment.");
    }
}
namespace SubliminalCart3lBot.Services;

public sealed record TelegramSettings(
    string BotToken,
    string WebhookSecret,
    string PublicBaseUrl,
    long OwnerTelegramId,
    string SupportUsername,
    string Terms);
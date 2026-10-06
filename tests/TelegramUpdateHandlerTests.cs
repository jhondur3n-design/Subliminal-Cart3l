using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SubliminalCart3lBot.Services;
using Xunit;

namespace SubliminalCart3lBot.Tests;

public sealed class TelegramUpdateHandlerTests
{
    private const string TestWebhookSecret = "local_test_secret_0123456789abcdef0123456789";

    [Fact]
    public async Task StartSendsWelcomeMessageAndInlineMenu()
    {
        await using var store = await TestStore.CreateAsync();
        using var fake = CreateHandler(store, out var handler);
        using var update = JsonDocument.Parse("""
            {"message":{"from":{"id":123,"username":"buyer","first_name":"Buyer"},"chat":{"id":123},"text":"/start"}}
            """);

        await handler.ProcessAsync(update.RootElement, CancellationToken.None);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("sendMessage", call.Method);
        using var request = JsonDocument.Parse(call.Json);
        Assert.Contains("Welcome to Subliminal Cart", request.RootElement.GetProperty("text").GetString());
        Assert.True(request.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ProductsSendsActiveProductsAsInlineButtons()
    {
        await using var store = await TestStore.CreateAsync();
        await store.Database.AddProductAsync("Night WAV", "A release", 19, "file-night");
        using var fake = CreateHandler(store, out var handler);
        using var update = JsonDocument.Parse("""
            {"message":{"from":{"id":123},"chat":{"id":123},"text":"/products"}}
            """);

        await handler.ProcessAsync(update.RootElement, CancellationToken.None);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("sendMessage", call.Method);
        using var request = JsonDocument.Parse(call.Json);
        var button = request.RootElement
            .GetProperty("reply_markup")
            .GetProperty("inline_keyboard")[0][0];
        Assert.Equal("product:1", button.GetProperty("callback_data").GetString());
        Assert.Contains("Night WAV", button.GetProperty("text").GetString());
    }

    [Fact]
    public async Task UnknownCommandDoesNotCallTelegramOrThrow()
    {
        await using var store = await TestStore.CreateAsync();
        using var fake = CreateHandler(store, out var handler);
        using var update = JsonDocument.Parse("""
            {"message":{"from":{"id":123},"chat":{"id":123},"text":"/not-a-command"}}
            """);

        await handler.ProcessAsync(update.RootElement, CancellationToken.None);

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task CallbackForMissingProductIsAnsweredSafely()
    {
        await using var store = await TestStore.CreateAsync();
        using var fake = CreateHandler(store, out var handler);
        using var update = JsonDocument.Parse("""
            {"callback_query":{"id":"callback-test","from":{"id":123},"message":{"chat":{"id":123}},"data":"product:999"}}
            """);

        await handler.ProcessAsync(update.RootElement, CancellationToken.None);

        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal("answerCallbackQuery", fake.Calls[0].Method);
        Assert.Equal("sendMessage", fake.Calls[1].Method);
        using var response = JsonDocument.Parse(fake.Calls[1].Json);
        Assert.Contains("no longer available", response.RootElement.GetProperty("text").GetString());
    }

    private static FakeTelegramHttpHandler CreateHandler(TestStore store, out TelegramUpdateHandler handler)
    {
        var settings = new TelegramSettings(
            "not-a-real-bot-token",
            TestWebhookSecret,
            "https://subliminalcart3l-001-site1.ctempurl.com",
            TelegramSettingsFactory.OwnerTelegramId,
            "",
            "Terms");
        var fake = new FakeTelegramHttpHandler();
        var telegram = new TelegramBotApi(new HttpClient(fake), settings);
        handler = new TelegramUpdateHandler(
            telegram,
            store.Database,
            store.Catalog,
            settings,
            NullLogger<TelegramUpdateHandler>.Instance);
        return fake;
    }
}
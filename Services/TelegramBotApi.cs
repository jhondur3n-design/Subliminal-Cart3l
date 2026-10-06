using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubliminalCart3lBot.Services;

public sealed class TelegramBotApi(HttpClient httpClient, TelegramSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task SendMessageAsync(
        long chatId,
        string text,
        object? replyMarkup = null,
        CancellationToken cancellationToken = default)
        => CallAsync("sendMessage", new
        {
            chatId,
            text,
            replyMarkup
        }, cancellationToken);

    public Task SendInvoiceAsync(
        long chatId,
        ProductItem product,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var title = product.Name.Length <= 32 ? product.Name : product.Name[..32];
        var description = $"Digital WAV product: {product.Name}";
        if (description.Length > 255)
        {
            description = description[..255];
        }

        return CallAsync("sendInvoice", new
        {
            chatId,
            title,
            description,
            payload,
            providerToken = "",
            currency = "XTR",
            prices = new[] { new { label = title, amount = product.StarsPrice } }
        }, cancellationToken);
    }

    public Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        CancellationToken cancellationToken = default)
        => CallAsync("answerCallbackQuery", new
        {
            callbackQueryId,
            text,
            showAlert = false
        }, cancellationToken);

    public Task AnswerPreCheckoutAsync(
        string preCheckoutQueryId,
        bool ok,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
        => CallAsync("answerPreCheckoutQuery", new
        {
            preCheckoutQueryId,
            ok,
            errorMessage
        }, cancellationToken);

    public Task SendDocumentAsync(
        long chatId,
        string fileId,
        string caption,
        CancellationToken cancellationToken = default)
        => CallAsync("sendDocument", new { chatId, document = fileId, caption }, cancellationToken);

    private async Task CallAsync(string method, object payload, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{settings.BotToken}/{method}",
                payload,
                JsonOptions,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"Telegram API {method} request failed.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Telegram API {method} returned HTTP {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var errorCode = json.RootElement.TryGetProperty("error_code", out var code)
                    ? code.GetInt32()
                    : 0;
                throw new InvalidOperationException($"Telegram API {method} failed with code {errorCode}.");
            }
        }
    }
}
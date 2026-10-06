using System.Globalization;
using System.Text.Json;

namespace SubliminalCart3lBot.Services;

public sealed class TelegramUpdateHandler(
    TelegramBotApi telegram,
    StoreDatabase database,
    ProductCatalog catalog,
    TelegramSettings settings,
    ILogger<TelegramUpdateHandler> logger)
{
    public async Task ProcessAsync(JsonElement update, CancellationToken cancellationToken)
    {
        if (update.TryGetProperty("pre_checkout_query", out var preCheckout))
        {
            await HandlePreCheckoutAsync(preCheckout, cancellationToken);
            return;
        }

        if (update.TryGetProperty("callback_query", out var callback))
        {
            await HandleCallbackAsync(callback, cancellationToken);
            return;
        }

        if (update.TryGetProperty("message", out var message))
        {
            await HandleMessageAsync(message, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var chatId = GetLong(message, "chat", "id");
        var userId = GetLong(message, "from", "id");
        if (chatId is null || userId is null)
        {
            return;
        }

        if (message.TryGetProperty("successful_payment", out var successfulPayment))
        {
            await HandleSuccessfulPaymentAsync(message, successfulPayment, chatId.Value, userId.Value, cancellationToken);
            return;
        }

        if (message.TryGetProperty("document", out var document) && userId == settings.OwnerTelegramId)
        {
            var fileName = GetString(document, "file_name") ?? "";
            var fileId = GetString(document, "file_id") ?? "";
            var productId = await catalog.CompleteWavUploadAsync(
                userId.Value,
                fileName,
                fileId,
                cancellationToken);
            await telegram.SendMessageAsync(
                chatId.Value,
                productId is null
                    ? "No WAV upload is pending, or the document does not have a .wav filename. Start again with /addproduct."
                    : $"WAV product #{productId} was added to the catalog.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = GetString(message, "text");
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('/'))
        {
            return;
        }

        var commandEnd = text.IndexOfAny([' ', '\n', '\r']);
        var command = (commandEnd < 0 ? text : text[..commandEnd]).Split('@')[0].ToLowerInvariant();
        var arguments = commandEnd < 0 ? "" : text[(commandEnd + 1)..].Trim();

        switch (command)
        {
            case "/start":
                await database.UpsertCustomerAsync(
                    userId.Value,
                    GetString(message, "from", "username"),
                    GetString(message, "from", "first_name") ?? "",
                    cancellationToken);
                await telegram.SendMessageAsync(
                    chatId.Value,
                    "Welcome to Subliminal Cart. Browse digital WAV releases and pay securely with Telegram Stars.",
                    MainMenu(),
                    cancellationToken);
                break;
            case "/help":
                await telegram.SendMessageAsync(
                    chatId.Value,
                    "Commands: /start, /products, /terms, /support <message>. The owner can add WAV products with /addproduct <stars> <name> | <description>.",
                    cancellationToken: cancellationToken);
                break;
            case "/products":
                await SendProductListAsync(chatId.Value, cancellationToken);
                break;
            case "/terms":
                await telegram.SendMessageAsync(chatId.Value, settings.Terms, cancellationToken: cancellationToken);
                break;
            case "/support":
                await HandleSupportAsync(chatId.Value, userId.Value, message, arguments, cancellationToken);
                break;
            case "/addproduct":
                await HandleAddProductAsync(chatId.Value, userId.Value, arguments, cancellationToken);
                break;
        }
    }

    private async Task HandleSupportAsync(
        long chatId,
        long userId,
        JsonElement message,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            var contact = string.IsNullOrWhiteSpace(settings.SupportUsername)
                ? "Send /support followed by your message to contact support."
                : $"For support, contact @{settings.SupportUsername} or send /support followed by your message.";
            await telegram.SendMessageAsync(chatId, contact, cancellationToken: cancellationToken);
            return;
        }

        var username = GetString(message, "from", "username") ?? "no-username";
        var firstName = GetString(message, "from", "first_name") ?? "Customer";
        await telegram.SendMessageAsync(
            settings.OwnerTelegramId,
            $"Support request from {firstName} (@{username}, Telegram ID {userId}):\n\n{arguments}",
            cancellationToken: cancellationToken);
        await telegram.SendMessageAsync(chatId, "Your support message has been sent.", cancellationToken: cancellationToken);
    }

    private async Task HandleAddProductAsync(
        long chatId,
        long userId,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (userId != settings.OwnerTelegramId)
        {
            await telegram.SendMessageAsync(chatId, "This command is not available.", cancellationToken: cancellationToken);
            return;
        }

        var fields = arguments.Split('|', 2, StringSplitOptions.TrimEntries);
        var details = fields[0].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2 || details.Length != 2
            || !int.TryParse(details[0], NumberStyles.None, CultureInfo.InvariantCulture, out var price)
            || price <= 0 || string.IsNullOrWhiteSpace(details[1])
            || string.IsNullOrWhiteSpace(fields[1]))
        {
            await telegram.SendMessageAsync(
                chatId,
                "Format: /addproduct <positive Stars price> <name> | <description>",
                cancellationToken: cancellationToken);
            return;
        }

        if (!catalog.BeginWavUpload(userId, new NewProduct(details[1], fields[1], price)))
        {
            await telegram.SendMessageAsync(chatId, "Finish or cancel the pending WAV upload first.", cancellationToken: cancellationToken);
            return;
        }

        await telegram.SendMessageAsync(chatId, "Now upload the product as a .wav document.", cancellationToken: cancellationToken);
    }

    private async Task SendProductListAsync(long chatId, CancellationToken cancellationToken)
    {
        var products = await catalog.GetAvailableAsync(cancellationToken);
        if (products.Count == 0)
        {
            await telegram.SendMessageAsync(chatId, "The catalog is empty. The owner can add WAV products with /addproduct.", cancellationToken: cancellationToken);
            return;
        }

        var rows = products.Select(product => new[]
        {
            new InlineButton($"{product.Name} · {product.StarsPrice} XTR", $"product:{product.Id}")
        }).ToArray();
        await telegram.SendMessageAsync(
            chatId,
            "Available digital WAV products:",
            new InlineKeyboardMarkup(rows),
            cancellationToken);
    }

    private async Task HandleCallbackAsync(JsonElement callback, CancellationToken cancellationToken)
    {
        var callbackId = GetString(callback, "id");
        var userId = GetLong(callback, "from", "id");
        var chatId = GetLong(callback, "message", "chat", "id");
        var data = GetString(callback, "data") ?? "";
        if (callbackId is null || userId is null)
        {
            return;
        }

        await telegram.AnswerCallbackAsync(callbackId, cancellationToken: cancellationToken);
        if (chatId is null)
        {
            return;
        }

        if (data == "products")
        {
            await SendProductListAsync(chatId.Value, cancellationToken);
        }
        else if (data == "terms")
        {
            await telegram.SendMessageAsync(chatId.Value, settings.Terms, cancellationToken: cancellationToken);
        }
        else if (data == "support")
        {
            var contact = string.IsNullOrWhiteSpace(settings.SupportUsername)
                ? "Send /support followed by your message to contact support."
                : $"Contact support at @{settings.SupportUsername} or use /support followed by your message.";
            await telegram.SendMessageAsync(chatId.Value, contact, cancellationToken: cancellationToken);
        }
        else if (data.StartsWith("product:", StringComparison.Ordinal)
            && long.TryParse(data.AsSpan("product:".Length), out var productId))
        {
            await SendProductDetailsAsync(chatId.Value, productId, cancellationToken);
        }
        else if (data.StartsWith("buy:", StringComparison.Ordinal)
            && long.TryParse(data.AsSpan("buy:".Length), out var buyProductId))
        {
            await CreateInvoiceAsync(chatId.Value, userId.Value, callback, buyProductId, cancellationToken);
        }
    }

    private async Task SendProductDetailsAsync(long chatId, long productId, CancellationToken cancellationToken)
    {
        var product = await catalog.FindAsync(productId, cancellationToken);
        if (product is null || !product.IsActive)
        {
            await telegram.SendMessageAsync(chatId, "This product is no longer available.", cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(
        [
            [new InlineButton($"Buy · {product.StarsPrice} XTR", $"buy:{product.Id}")],
            [new InlineButton("Browse products", "products")]
        ]);
        await telegram.SendMessageAsync(
            chatId,
            $"{product.Name}\n\n{product.Description}\n\nPrice: {product.StarsPrice} XTR",
            keyboard,
            cancellationToken);
    }

    private async Task CreateInvoiceAsync(
        long chatId,
        long userId,
        JsonElement callback,
        long productId,
        CancellationToken cancellationToken)
    {
        var product = await catalog.FindAsync(productId, cancellationToken);
        if (product is null || !product.IsActive || string.IsNullOrWhiteSpace(product.TelegramFileId))
        {
            await telegram.SendMessageAsync(chatId, "This product is no longer available.", cancellationToken: cancellationToken);
            return;
        }

        await database.UpsertCustomerAsync(
            userId,
            GetString(callback, "from", "username"),
            GetString(callback, "from", "first_name") ?? "",
            cancellationToken);
        var payload = await database.CreatePendingOrderAsync(userId, product, cancellationToken);
        try
        {
            await telegram.SendInvoiceAsync(chatId, product, payload, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            logger.LogError("Telegram invoice creation failed for an order.");
            await telegram.SendMessageAsync(chatId, "The invoice could not be created. Please try again later.", cancellationToken: cancellationToken);
        }
    }

    private async Task HandlePreCheckoutAsync(JsonElement query, CancellationToken cancellationToken)
    {
        var queryId = GetString(query, "id");
        var userId = GetLong(query, "from", "id");
        var payload = GetString(query, "invoice_payload");
        var currency = GetString(query, "currency") ?? "";
        var amount = GetInt(query, "total_amount");
        if (queryId is null || userId is null || payload is null || amount is null)
        {
            return;
        }

        var validation = await database.ValidateCheckoutAsync(
            payload,
            userId.Value,
            currency,
            amount.Value,
            cancellationToken);
        await telegram.AnswerPreCheckoutAsync(
            queryId,
            validation.IsValid,
            validation.IsValid ? null : validation.ErrorMessage,
            cancellationToken);
    }

    private async Task HandleSuccessfulPaymentAsync(
        JsonElement message,
        JsonElement payment,
        long chatId,
        long userId,
        CancellationToken cancellationToken)
    {
        var payload = GetString(payment, "invoice_payload");
        var currency = GetString(payment, "currency") ?? "";
        var amount = GetInt(payment, "total_amount");
        var chargeId = GetString(payment, "telegram_payment_charge_id");
        if (payload is null || amount is null || chargeId is null)
        {
            return;
        }

        var completion = await database.CompletePaymentAsync(
            payload,
            userId,
            currency,
            amount.Value,
            chargeId,
            cancellationToken);
        if (completion.Result is not ("paid" or "duplicate"))
        {
            await telegram.SendMessageAsync(chatId, "Payment received, but the order needs support attention.", cancellationToken: cancellationToken);
            return;
        }

        if (!await database.TryClaimDeliveryAsync(completion.OrderId, cancellationToken))
        {
            if (completion.Result == "duplicate")
            {
                await telegram.SendMessageAsync(chatId, "This payment has already been processed.", cancellationToken: cancellationToken);
            }
            return;
        }

        try
        {
            await telegram.SendDocumentAsync(
                chatId,
                completion.TelegramFileId,
                $"Your WAV purchase: {completion.ProductName}",
                cancellationToken);
            await database.SetDeliveryStatusAsync(completion.OrderId, true, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            logger.LogError("Digital product delivery failed for order {OrderId}.", completion.OrderId);
            await database.SetDeliveryStatusAsync(completion.OrderId, false, cancellationToken);
            await telegram.SendMessageAsync(
                chatId,
                "Payment confirmed, but delivery failed. Please contact support with your order details.",
                cancellationToken: cancellationToken);
        }
    }

    private static InlineKeyboardMarkup MainMenu() => new(
    [
        [new InlineButton("Browse products", "products")],
        [new InlineButton("Terms", "terms"), new InlineButton("Support", "support")]
    ]);

    private static string? GetString(JsonElement element, params string[] path)
    {
        foreach (var property in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out element))
            {
                return null;
            }
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static long? GetLong(JsonElement element, params string[] path)
    {
        foreach (var property in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out element))
            {
                return null;
            }
        }
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value) ? value : null;
    }

    private static int? GetInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
}

public sealed record InlineButton(string Text, string CallbackData);

public sealed class InlineKeyboardMarkup(InlineButton[][] inlineKeyboard)
{
    public InlineButton[][] InlineKeyboard { get; } = inlineKeyboard;
}
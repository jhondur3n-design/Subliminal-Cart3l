using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SubliminalCart3lBot.Services;

var builder = WebApplication.CreateBuilder(args);
var telegramSettings = TelegramSettingsFactory.Create(builder.Configuration);
var databasePath = builder.Configuration["Storage:DatabasePath"] ?? "App_Data/subliminalcart3l.db";

builder.Services.AddSingleton(telegramSettings);
builder.Services.AddSingleton(new StoreDatabase(builder.Environment.ContentRootPath, databasePath));
builder.Services.AddSingleton<ProductCatalog>();
builder.Services.AddSingleton<TelegramUpdateHandler>();
builder.Services.AddHttpClient<TelegramBotApi>();

var app = builder.Build();
app.UseExceptionHandler(errorHandler => errorHandler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Request processing failed." });
}));

await app.Services.GetRequiredService<StoreDatabase>().InitializeAsync();

app.MapGet("/", () => Results.Ok(new
{
    status = "online",
    service = "SubliminalCart3lBot"
}));

app.MapGet("/api/telegram/status", () => Results.Ok(new
{
    status = "online",
    webhookEndpoint = $"{telegramSettings.PublicBaseUrl}/api/telegram/webhook"
}));

app.MapPost("/api/telegram/webhook", async (
    HttpRequest request,
    TelegramUpdateHandler updateHandler,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var suppliedSecret = request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
    var suppliedBytes = Encoding.UTF8.GetBytes(suppliedSecret);
    var expectedBytes = Encoding.UTF8.GetBytes(telegramSettings.WebhookSecret);
    if (suppliedBytes.Length == 0 || !CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
    {
        return Results.Unauthorized();
    }

    try
    {
        using var update = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        if (update.RootElement.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest();
        }

        await updateHandler.ProcessAsync(update.RootElement, cancellationToken);
        return Results.Ok(new { ok = true });
    }
    catch (JsonException)
    {
        logger.LogWarning("Telegram webhook received invalid JSON.");
        return Results.BadRequest();
    }
});

await app.RunAsync();
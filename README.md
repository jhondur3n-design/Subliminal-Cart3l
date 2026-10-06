# Subliminal Cart 3L Telegram Bot

ASP.NET Core Minimal API on .NET 10. It receives Telegram updates through an HTTPS webhook, stores customers, products, and orders in SQLite, accepts Telegram Stars (`XTR`), and delivers WAV files after verified payment. It does not require Python or Docker.

## Features

- `GET /` and `GET /api/telegram/status` online checks.
- Secret-validated `POST /api/telegram/webhook`.
- `/start`, `/help`, `/products`, `/terms`, and `/support` Telegram commands.
- Inline product and purchase buttons with callback-query handling.
- Telegram Stars invoices, pre-checkout validation, and successful-payment handling.
- Unique invoice payload and Telegram charge IDs with atomic pending-to-paid transitions to prevent duplicate fulfillment.
- Owner-only product setup for Telegram ID `6197139693`: send `/addproduct <stars> <name> | <description>`, then upload the WAV as a `.wav` document.
- SQLite persistence in `App_Data/subliminalcart3l.db` by default.

## Configuration

The committed `appsettings.json` contains only non-secret defaults. Never put the BotFather token or webhook secret in source control, `appsettings.json`, or `appsettings.example.json`.

Required ASP.NET environment variables (SmarterASP application settings use `__` for configuration `:`):

| Environment variable | Configuration key | Required value |
| --- | --- | --- |
| `Telegram__BotToken` | `Telegram:BotToken` | BotFather token. Enter only in SmarterASP's secure application/environment settings. |
| `Telegram__WebhookSecret` | `Telegram:WebhookSecret` | Random 32-256 character secret using letters, numbers, `_`, or `-`. Keep private. |
| `Telegram__PublicBaseUrl` | `Telegram:PublicBaseUrl` | `https://subliminalcart3l-001-site1.ctempurl.com/` |

The owner ID is fixed in the application at `6197139693`. Optional settings are `Store__SupportUsername`, `Store__Terms`, and `Storage__DatabasePath`. The database path defaults to `App_Data/subliminalcart3l.db`; make sure the IIS app identity has write access to `App_Data` and that this directory persists across deployments.

## Build

Install the .NET 10 SDK, then run from the repository root:

```bash
dotnet restore SubliminalCart3lBot.csproj --runtime win-x64
dotnet build SubliminalCart3lBot.csproj --configuration Release --runtime win-x64 --no-restore
dotnet publish SubliminalCart3lBot.csproj --configuration Release --runtime win-x64 --self-contained false --no-build --no-restore --output publish
```

## Automated Tests

The root solution includes the production app and `tests/SubliminalCart3lBot.Tests.csproj`. Tests use isolated temporary SQLite databases and a fake Telegram HTTP handler; they do not call Telegram, use a real BotFather token, or make payments. Run the full check from the repository root:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## SmarterASP.NET Deployment

1. Create/select a SmarterASP.NET site that supports ASP.NET Core .NET 10 and HTTPS. Configure the site's HTTPS certificate and confirm the site root is mapped to its ASP.NET Core application directory.
2. Build and publish using the commands above. Upload the **contents** of `publish/` to the site's application root, including the generated `web.config`, `SubliminalCart3lBot.exe`, `SubliminalCart3lBot.dll`, `.deps.json`, `.runtimeconfig.json`, appsettings files, and published dependencies. Do not deploy the old Python files, a Docker image, or only the source `.cs` files.
3. In SmarterASP.NET's secure application/environment settings, create `Telegram__BotToken`, `Telegram__WebhookSecret`, and `Telegram__PublicBaseUrl` with the values described above. Enter the BotFather token directly into the secure `Telegram__BotToken` setting; do not paste it into a file or command history. Generate a random webhook secret and enter it directly into `Telegram__WebhookSecret`. Do not send either secret in logs, support requests, or source control.
4. Set `ASPNETCORE_ENVIRONMENT` to `Production` if the control panel does not already do so. Ensure `App_Data` is writable and persistent. If changing the database location, create `Storage__DatabasePath` with a writable path.
5. Restart/recycle the site from the SmarterASP.NET control panel after publishing and saving settings. The application creates its SQLite tables at startup.
6. Verify the deployed site responds successfully before configuring Telegram:

   ```text
   GET https://subliminalcart3l-001-site1.ctempurl.com/
   ```

   Expected JSON: `{"status":"online","service":"SubliminalCart3lBot"}`. The status endpoint is `https://subliminalcart3l-001-site1.ctempurl.com/api/telegram/status`.
7. Only after deployment and the root check succeed, register the Telegram webhook from a trusted machine where the BotFather token and the exact same webhook secret are available as shell variables `BOT_TOKEN` and `TELEGRAM_WEBHOOK_SECRET`:

   ```bash
   curl --fail-with-body --silent --show-error --request POST \
     "https://api.telegram.org/bot${BOT_TOKEN}/setWebhook" \
     --data-urlencode "url=https://subliminalcart3l-001-site1.ctempurl.com/api/telegram/webhook" \
     --data-urlencode "secret_token=${TELEGRAM_WEBHOOK_SECRET}"
   ```

The application does not register the webhook at startup. Do not run the registration command before the site is deployed and healthy.

## Project Files

- `SubliminalCart3lBot.csproj`: .NET 10 web project and SQLite package references.
- `SubliminalCart3lBot.slnx`: root solution used by restore, build, and test commands.
- `Program.cs`: configuration validation, dependency registration, health routes, and webhook route.
- `Services/`: Telegram Bot API client, update processing, `ProductCatalog`, and persistent store/order logic.
- `tests/`: isolated catalog, payment, update-handler, and configuration tests.
- `appsettings.json`: committed non-secret defaults.
- `appsettings.example.json`: secret-free configuration template.
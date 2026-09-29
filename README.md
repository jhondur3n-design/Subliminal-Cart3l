# Subliminal Cart Telegram Store

An async Telegram digital-product store using Python 3.12+, `python-telegram-bot`, Telegram Stars (XTR), SQLAlchemy, and SQLite. The bot uses polling for local or single-process deployment; the application lifecycle is kept separate so a webhook runner can be added later.

## Requirements

- Python 3.12 or newer
- A Telegram bot token from [@BotFather](https://t.me/BotFather)
- Your Telegram numeric user ID for the administrator setting

## Setup

1. Create a bot with BotFather using `/newbot` and copy its token. Keep the token private.
2. Optionally set a bot description with `/setdescription` and a command menu with `/setcommands` (`start - Open the store`, `help - Show the store menu`, `admin - Admin panel`). Admin commands remain protected by the configured numeric ID. Inline mode is not required. Telegram Stars payments use currency `XTR` and an empty provider token, so no external payment provider is needed.
3. Create and activate a virtual environment, then install dependencies:

	```bash
	python3.12 -m venv .venv
	source .venv/bin/activate
	python -m pip install -r requirements.txt
	```

4. Copy `.env.example` to `.env` and set `BOT_TOKEN`, `ADMIN_TELEGRAM_ID`, and (optionally) `SUPPORT_USERNAME`. Use your numeric Telegram account ID, not your username.
5. Start the bot:

	```bash
	python bot.py
	```

	The SQLite database and tables are created automatically on startup. The default database is `store.db` in the project directory. Set `DATABASE_URL` to change it.

## Admin operations

Open the bot as the account whose ID is configured in `ADMIN_TELEGRAM_ID`, then use `/admin` for the command list. `/addproduct` asks for a name, description, positive whole-number Stars price, and product document. The bot stores the document's Telegram `file_id`; it can also accept a file ID instead of an upload. Use `/products` to edit a product's price, description, delivery file, or active status. Other commands are `/orders`, `/customers`, `/stats`, and `/broadcast`. `/cancel` ends an in-progress admin prompt. Products do not require Python code changes.

Keep the original uploaded document in your bot's account and avoid deleting the message/file it came from. The bot delivers using Telegram's saved file ID.

## Customer and payment flow

Customers use `/start` to register and see Store, My Purchases, and Support. They can browse active products, open descriptions, and receive an XTR invoice. The bot stores a pending order before sending the invoice. Telegram's pre-checkout event must match the buyer, pending payload, active product, saved price, currency, and invoice age. A file is sent only after Telegram sends `successful_payment` and the bot verifies the confirmed payer, payload, XTR amount, currency, and unique payment charge ID. Paid purchases can be sent again from My Purchases.

For testing, use a private bot chat and complete an invoice with Telegram Stars using an account with an available Stars balance. Telegram does not provide a local fake-payment switch for Stars; make sure to understand any real Stars expenditure before testing. For a no-charge smoke test, use `/start`, browse the store, and test admin product management without completing checkout.

## Tests

```bash
python -m pytest -q
```

GitHub Actions runs this test suite on pushes and pull requests. Actions is CI only, not a bot host.

## Deployment

Run the bot on an always-on Python host or VPS. Keep `.env` private, provision persistent storage for the SQLite database, install dependencies, and run `python bot.py` under a process supervisor such as systemd or a container restart policy. Back up the database and monitor the process logs. Do not use GitHub Actions as a 24/7 server. For horizontal or multi-instance deployment, move from SQLite to a server database and add suitable delivery/job coordination before running multiple workers.

The handlers and database initialization are registered through the PTB `Application` lifecycle; polling currently starts in `bot.py`. A future webhook deployment can supply a webhook runner and HTTPS endpoint while reusing the same handlers and persistence layer.
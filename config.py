import os
from pathlib import Path

from dotenv import load_dotenv

load_dotenv()

BOT_TOKEN = os.getenv("BOT_TOKEN", "").strip()
ADMIN_TELEGRAM_ID = int(os.getenv("ADMIN_TELEGRAM_ID", "0"))
DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///./store.db")
SUPPORT_USERNAME = os.getenv("SUPPORT_USERNAME", "").strip().lstrip("@")
LOG_LEVEL = os.getenv("LOG_LEVEL", "INFO").upper()


def validate_config() -> None:
    if not BOT_TOKEN:
        raise RuntimeError("BOT_TOKEN is required. Set it in your .env file.")
    if ADMIN_TELEGRAM_ID <= 0:
        raise RuntimeError("ADMIN_TELEGRAM_ID must be a positive Telegram user ID.")
    if DATABASE_URL.startswith("sqlite"):
        database_path = DATABASE_URL.rsplit("///", maxsplit=1)[-1]
        if database_path not in {":memory:", ""}:
            Path(database_path).expanduser().parent.mkdir(parents=True, exist_ok=True)
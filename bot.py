import logging

from sqlalchemy import select
from telegram import Update
from telegram.ext import (
    Application,
    CallbackQueryHandler,
    CommandHandler,
    ContextTypes,
    MessageHandler,
    PreCheckoutQueryHandler,
    filters,
)

from admin import register_admin_handlers
from config import ADMIN_TELEGRAM_ID, BOT_TOKEN, LOG_LEVEL, SUPPORT_USERNAME, validate_config
from database import init_database, session_scope
from keyboards import main_menu
from models import User, utc_now
from payments import pre_checkout_handler, successful_payment_handler
from products import (
    buy_product_handler,
    product_detail_handler,
    resend_purchase_handler,
    show_purchases,
    show_store,
)

logging.basicConfig(
    level=getattr(logging, LOG_LEVEL, logging.INFO),
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


async def post_init(application: Application) -> None:
    await init_database()
    logger.info("Database initialized")


async def start_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    user = update.effective_user
    if user is None:
        return
    async with session_scope() as session:
        record = await session.scalar(select(User).where(User.telegram_id == user.id))
        if record is None:
            record = User(telegram_id=user.id, username=user.username, first_name=user.first_name)
            session.add(record)
        else:
            record.username = user.username
            record.first_name = user.first_name
            record.last_seen_at = utc_now()
        await session.commit()
    await update.effective_message.reply_text(
        f"Welcome, {user.first_name}! Choose an option below.", reply_markup=main_menu()
    )


async def support_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if SUPPORT_USERNAME:
        text = f"For support, contact @{SUPPORT_USERNAME}."
    else:
        context.user_data["awaiting_support"] = True
        text = "Send your question in your next message and it will be forwarded to support."
    await update.effective_message.reply_text(text, reply_markup=main_menu())


async def support_message_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    user = update.effective_user
    message = update.effective_message
    if (
        user is None
        or message is None
        or user.id == ADMIN_TELEGRAM_ID
        or not context.user_data.get("awaiting_support")
    ):
        return
    text = message.text.strip() if message.text else ""
    if not text:
        await message.reply_text("Please send your support request as text.")
        return
    await context.bot.send_message(
        chat_id=ADMIN_TELEGRAM_ID,
        text=f"Support message from {user.first_name} (@{user.username or 'no-username'}, ID {user.id}):\n\n{text}",
    )
    context.user_data.pop("awaiting_support", None)
    await message.reply_text("Your message has been sent to support.", reply_markup=main_menu())


def build_application() -> Application:
    application = Application.builder().token(BOT_TOKEN).post_init(post_init).build()
    application.add_handler(CommandHandler("start", start_handler))
    application.add_handler(CommandHandler("help", start_handler))
    application.add_handler(CallbackQueryHandler(show_store, pattern=r"^products:list$"))
    application.add_handler(CallbackQueryHandler(product_detail_handler, pattern=r"^product:\d+$"))
    application.add_handler(CallbackQueryHandler(buy_product_handler, pattern=r"^buy:\d+$"))
    application.add_handler(CallbackQueryHandler(resend_purchase_handler, pattern=r"^purchase:\d+$"))
    application.add_handler(MessageHandler(filters.SUCCESSFUL_PAYMENT, successful_payment_handler))
    application.add_handler(MessageHandler(filters.Regex(r"^(?:🛍️\s*Store|Store)$"), show_store))
    application.add_handler(
        MessageHandler(filters.Regex(r"^(?:📦\s*My Purchases|My Purchases)$"), show_purchases)
    )
    application.add_handler(MessageHandler(filters.Regex(r"^(?:💬\s*Support|Support)$"), support_handler))
    application.add_handler(
        MessageHandler(filters.TEXT & ~filters.COMMAND, support_message_handler), group=1
    )
    application.add_handler(PreCheckoutQueryHandler(pre_checkout_handler))
    register_admin_handlers(application)
    return application


def main() -> None:
    validate_config()
    application = build_application()
    application.run_polling(allowed_updates=Update.ALL_TYPES)


if __name__ == "__main__":
    main()
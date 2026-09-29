import logging

from sqlalchemy import func, select
from sqlalchemy.orm import selectinload
from telegram import InlineKeyboardButton, InlineKeyboardMarkup, Update
from telegram.ext import Application, CallbackQueryHandler, CommandHandler, ContextTypes, MessageHandler, filters

from config import ADMIN_TELEGRAM_ID
from database import session_scope
from models import Order, Product, User, utc_now

logger = logging.getLogger(__name__)


def is_admin(update: Update) -> bool:
    return bool(update.effective_user and update.effective_user.id == ADMIN_TELEGRAM_ID)


async def admin_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    await update.effective_message.reply_text(
        "✨ Premium Admin Control Center\n\n"
        "• /addproduct — add a new digital item\n"
        "• /products — manage products\n"
        "• /orders — track recent sales\n"
        "• /customers — review buyers\n"
        "• /stats — see revenue and activity\n"
        "• /broadcast — send a message to customers\n"
        "• /cancel — exit the current admin action"
    )


async def cancel_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if is_admin(update):
        context.user_data.pop("admin_state", None)
        await update.effective_message.reply_text("Admin action cancelled.")


async def add_product_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    context.user_data["admin_state"] = {"action": "add_name"}
    await update.effective_message.reply_text("Send the product name, or /cancel.")


async def products_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    async with session_scope() as session:
        products = (await session.scalars(select(Product).order_by(Product.id))).all()
    if not products:
        await update.effective_message.reply_text("📦 No products yet. Use /addproduct to launch your first item.")
        return
    await update.effective_message.reply_text(
        "🛍️ Premium catalog\n\n" + "\n".join(
            f"• #{item.id} {item.name} | {item.price_xtr} XTR | {'active' if item.is_active else 'inactive'}"
            for item in products
        )
    )
    for item in products:
        keyboard = InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton("💰 Price", callback_data=f"admin:price:{item.id}"),
                    InlineKeyboardButton("📝 Description", callback_data=f"admin:desc:{item.id}"),
                ],
                [
                    InlineKeyboardButton("📁 Replace file", callback_data=f"admin:file:{item.id}"),
                    InlineKeyboardButton("🔄 Toggle", callback_data=f"admin:toggle:{item.id}"),
                ],
            ]
        )
        await update.effective_message.reply_text(
            f"🧩 Manage product #{item.id}: {item.name}",
            reply_markup=keyboard,
        )


async def orders_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    async with session_scope() as session:
        orders = (
            await session.scalars(
                select(Order).options(selectinload(Order.user), selectinload(Order.product))
                .order_by(Order.created_at.desc()).limit(20)
            )
        ).all()
    if not orders:
        await update.effective_message.reply_text("📦 No orders yet.")
        return
    lines = [
        f"• #{order.id} {order.product.name} | {order.amount_xtr} XTR | {order.status} | "
        f"delivery {order.delivery_status} · @{order.user.username or 'no-username'}"
        for order in orders
    ]
    await update.effective_message.reply_text("📈 Recent order activity\n\n" + "\n".join(lines))


async def customers_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    async with session_scope() as session:
        customers = (await session.scalars(select(User).order_by(User.last_seen_at.desc()).limit(30))).all()
    if not customers:
        await update.effective_message.reply_text("👥 No customers yet.")
        return
    await update.effective_message.reply_text(
        "👥 Recent customers\n\n" + "\n".join(
            f"• {item.first_name} (@{item.username or 'no-username'}) · ID {item.telegram_id}"
            for item in customers
        )
    )


async def stats_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    async with session_scope() as session:
        paid_count = await session.scalar(select(func.count(Order.id)).where(Order.status == "paid")) or 0
        revenue = await session.scalar(
            select(func.coalesce(func.sum(Order.amount_xtr), 0)).where(Order.status == "paid")
        ) or 0
        customer_count = await session.scalar(select(func.count(User.id))) or 0
        product_count = await session.scalar(select(func.count(Product.id)).where(Product.is_active.is_(True))) or 0
    await update.effective_message.reply_text(
        "📊 Store performance\n\n"
        f"• Paid orders: {paid_count}\n"
        f"• Revenue: {revenue} XTR\n"
        f"• Customers: {customer_count}\n"
        f"• Active products: {product_count}"
    )


async def broadcast_command(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        await update.effective_message.reply_text("This command is not available.")
        return
    context.user_data["admin_state"] = {"action": "broadcast"}
    await update.effective_message.reply_text("Send the broadcast text, or /cancel.")


async def admin_text_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        return
    state = context.user_data.get("admin_state")
    if not state:
        return
    text = update.effective_message.text.strip()
    action = state["action"]
    if action == "broadcast":
        async with session_scope() as session:
            recipients = list(await session.scalars(select(User.telegram_id)))
        sent = 0
        for telegram_id in recipients:
            try:
                await context.bot.send_message(chat_id=telegram_id, text=text)
                sent += 1
            except Exception:
                logger.info("Broadcast could not reach Telegram user %s", telegram_id)
        context.user_data.pop("admin_state", None)
        await update.effective_message.reply_text(f"Broadcast sent to {sent} of {len(recipients)} customers.")
    elif action == "add_name":
        state.update(action="add_description", name=text)
        await update.effective_message.reply_text("Send the product description.")
    elif action == "add_description":
        state.update(action="add_price", description=text)
        await update.effective_message.reply_text("Send the price as a positive whole number of Telegram Stars.")
    elif action in {"add_price", "edit_price"}:
        try:
            price = int(text)
            if price <= 0:
                raise ValueError
        except ValueError:
            await update.effective_message.reply_text("Price must be a positive whole number of Stars.")
            return
        if action == "add_price":
            state.update(action="add_file", price=price)
            await update.effective_message.reply_text("Upload the product as a document, or send its Telegram file ID.")
        else:
            async with session_scope() as session:
                product = await session.get(Product, state["product_id"])
                if product:
                    product.price_xtr = price
                    product.updated_at = utc_now()
                    await session.commit()
            context.user_data.pop("admin_state", None)
            await update.effective_message.reply_text("Product price updated.")
    elif action in {"add_file", "edit_file"}:
        await save_product_file(update, context, text)
    elif action == "edit_description":
        async with session_scope() as session:
            product = await session.get(Product, state["product_id"])
            if product:
                product.description = text
                product.updated_at = utc_now()
                await session.commit()
        context.user_data.pop("admin_state", None)
        await update.effective_message.reply_text("Product description updated.")


async def save_product_file(update: Update, context: ContextTypes.DEFAULT_TYPE, file_id: str) -> None:
    state = context.user_data.get("admin_state", {})
    if state["action"] == "add_file":
        async with session_scope() as session:
            product = Product(
                name=state["name"], description=state["description"],
                price_xtr=state["price"], telegram_file_id=file_id, is_active=True,
            )
            session.add(product)
            await session.commit()
            product_id = product.id
        context.user_data.pop("admin_state", None)
        await update.effective_message.reply_text(f"Product #{product_id} added and activated.")
    else:
        async with session_scope() as session:
            product = await session.get(Product, state["product_id"])
            if product:
                product.telegram_file_id = file_id
                product.updated_at = utc_now()
                await session.commit()
        context.user_data.pop("admin_state", None)
        await update.effective_message.reply_text("Product file updated.")


async def admin_document_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not is_admin(update):
        return
    state = context.user_data.get("admin_state", {})
    if state.get("action") not in {"add_file", "edit_file"}:
        return
    document = update.effective_message.document
    if document is not None:
        await save_product_file(update, context, document.file_id)


async def admin_callback_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    if not is_admin(update):
        await query.answer("Not authorized.", show_alert=True)
        return
    await query.answer()
    _, action, product_id_text = query.data.split(":", maxsplit=2)
    product_id = int(product_id_text)
    async with session_scope() as session:
        product = await session.get(Product, product_id)
        if product is None:
            await query.edit_message_text("Product not found.")
            return
        if action == "toggle":
            product.is_active = not product.is_active
            product.updated_at = utc_now()
            await session.commit()
            await query.edit_message_text(f"{product.name} is now {'active' if product.is_active else 'inactive'}.")
            return
    if action == "price":
        context.user_data["admin_state"] = {"action": "edit_price", "product_id": product_id}
        await query.message.reply_text("Send the new positive whole-number price in Stars, or /cancel.")
    elif action == "desc":
        context.user_data["admin_state"] = {"action": "edit_description", "product_id": product_id}
        await query.message.reply_text("Send the new product description, or /cancel.")
    elif action == "file":
        context.user_data["admin_state"] = {"action": "edit_file", "product_id": product_id}
        await query.message.reply_text("Upload a replacement document or send its Telegram file ID, or /cancel.")


def register_admin_handlers(application: Application) -> None:
    for command, callback in [
        ("admin", admin_command), ("addproduct", add_product_command),
        ("products", products_command), ("orders", orders_command),
        ("customers", customers_command), ("stats", stats_command),
        ("broadcast", broadcast_command), ("cancel", cancel_command),
    ]:
        application.add_handler(CommandHandler(command, callback))
    application.add_handler(CallbackQueryHandler(admin_callback_handler, pattern=r"^admin:"), group=1)
    application.add_handler(MessageHandler(filters.Document.ALL, admin_document_handler), group=-1)
    application.add_handler(MessageHandler(filters.TEXT & ~filters.COMMAND, admin_text_handler), group=-1)
import logging
from uuid import uuid4

from sqlalchemy import select
from sqlalchemy.orm import selectinload
from telegram import LabeledPrice, Update
from telegram.ext import ContextTypes

from database import session_scope
from delivery import deliver_order
from keyboards import main_menu, product_detail, product_list, purchase_list
from models import Order, Product, User

logger = logging.getLogger(__name__)


async def show_store(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    async with session_scope() as session:
        products = (
            await session.scalars(
                select(Product).where(Product.is_active.is_(True)).order_by(Product.id)
            )
        ).all()
    message = update.effective_message
    query = update.callback_query
    if not products:
        empty_text = "✨ The premium collection is empty right now."
        if query:
            await query.answer()
            await query.edit_message_text(empty_text)
        elif message:
            await message.reply_text(empty_text, reply_markup=main_menu())
        return
    buttons = product_list([(item.id, item.name, item.price_xtr) for item in products])
    intro_text = "✨ Welcome to the premium store.\n\nChoose a product below and unlock instant access."
    if query:
        await query.answer()
        await query.edit_message_text(intro_text, reply_markup=buttons)
    elif message:
        await message.reply_text(intro_text, reply_markup=buttons)


async def product_detail_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    await query.answer()
    product_id = int(query.data.split(":", maxsplit=1)[1])
    async with session_scope() as session:
        product = await session.get(Product, product_id)
        if product is None or not product.is_active:
            await query.edit_message_text("This product is no longer available.")
            return
        text = (
            f"✨ {product.name}\n\n"
            f"{product.description}\n\n"
            f"💎 Price: {product.price_xtr} XTR\n"
            f"⚡ Instant delivery • Secure Telegram Stars"
        )
    await query.edit_message_text(text, reply_markup=product_detail(product_id))


async def buy_product_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    user = update.effective_user
    await query.answer()
    if user is None:
        return
    product_id = int(query.data.split(":", maxsplit=1)[1])
    async with session_scope() as session:
        product = await session.get(Product, product_id)
        customer = await session.scalar(select(User).where(User.telegram_id == user.id))
        if product is None or not product.is_active or not product.telegram_file_id:
            await query.edit_message_text("This product is no longer available.")
            return
        if customer is None:
            await query.edit_message_text("Please use /start before placing an order.")
            return
        payload = f"order:{uuid4().hex}"
        order = Order(
            invoice_payload=payload,
            user_id=customer.id,
            product_id=product.id,
            amount_xtr=product.price_xtr,
            currency="XTR",
            status="pending",
        )
        session.add(order)
        await session.commit()
        amount = product.price_xtr
        name = product.name
        order_id = order.id
    try:
        await context.bot.send_invoice(
            chat_id=user.id,
            title=name[:32],
            description=f"Digital product: {name}"[:255],
            payload=payload,
            provider_token="",
            currency="XTR",
            prices=[LabeledPrice(label=name[:32], amount=amount)],
        )
    except Exception:
        logger.exception("Could not create invoice for order %s", order_id)
        async with session_scope() as session:
            failed_order = await session.get(Order, order_id)
            if failed_order:
                failed_order.status = "canceled"
                await session.commit()
        await query.edit_message_text("Could not create the invoice. Please try again later.")
        return
    try:
        await query.edit_message_text("✅ Your invoice is ready in the chat. Complete payment to unlock your order.")
    except Exception:
        logger.debug("Invoice sent; product message could not be edited for order %s", order_id)


async def show_purchases(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    user = update.effective_user
    message = update.effective_message
    if user is None or message is None:
        return
    async with session_scope() as session:
        customer = await session.scalar(select(User).where(User.telegram_id == user.id))
        if customer is None:
            await message.reply_text("Use /start to open your store account.", reply_markup=main_menu())
            return
        orders = (
            await session.scalars(
                select(Order)
                .where(Order.user_id == customer.id, Order.status == "paid")
                .order_by(Order.paid_at.desc())
                .limit(20)
            )
        ).all()
        entries = []
        for order in orders:
            product = await session.get(Product, order.product_id)
            entries.append(
                (order.id, product.name if product else "Product", order.delivery_status)
            )
    if not entries:
        await message.reply_text("📦 You do not have any purchases yet.", reply_markup=main_menu())
        return
    await message.reply_text(
        "📦 Your premium library\n\nSelect a purchase to receive a fresh copy.",
        reply_markup=purchase_list(entries),
    )


async def resend_purchase_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    user = update.effective_user
    await query.answer()
    if user is None:
        return
    order_id = int(query.data.split(":", maxsplit=1)[1])
    async with session_scope() as session:
        order = await session.scalar(
            select(Order)
            .options(selectinload(Order.user))
            .where(Order.id == order_id, Order.status == "paid")
        )
        if order is None or order.user.telegram_id != user.id:
            await query.edit_message_text("That purchase is not available in your account.")
            return
    delivered = await deliver_order(context.bot, user.id, order_id, resend=True)
    await query.edit_message_text(
        "✅ Your file has been sent again."
        if delivered
        else "⚠️ Delivery could not be completed. Please contact support."
    )
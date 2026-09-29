import logging
from datetime import timedelta

from sqlalchemy import select
from sqlalchemy.orm import selectinload
from telegram import Update
from telegram.ext import ContextTypes

from database import session_scope
from delivery import deliver_order
from models import Order, Product, utc_now

logger = logging.getLogger(__name__)
PRE_CHECKOUT_MAX_AGE = timedelta(hours=24)


def valid_payment_details(
    *,
    order: Order,
    telegram_user_id: int,
    payload: str,
    currency: str,
    total_amount: int,
) -> bool:
    return (
        order.status == "pending"
        and order.user.telegram_id == telegram_user_id
        and order.invoice_payload == payload
        and order.currency == currency == "XTR"
        and order.amount_xtr == total_amount
    )


async def pre_checkout_handler(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.pre_checkout_query
    if query is None:
        return
    async with session_scope() as session:
        result = await session.execute(
            select(Order, Product)
            .join(Product, Order.product_id == Product.id)
            .options(selectinload(Order.user))
            .where(Order.invoice_payload == query.invoice_payload)
        )
        row = result.one_or_none()
        if row is None:
            await query.answer(ok=False, error_message="This invoice is no longer available.")
            return
        order, product = row
        created_at = order.created_at
        if created_at.tzinfo is None:
            created_at = created_at.replace(tzinfo=utc_now().tzinfo)
        age_valid = utc_now() - created_at <= PRE_CHECKOUT_MAX_AGE
        valid = (
            valid_payment_details(
                order=order,
                telegram_user_id=query.from_user.id,
                payload=query.invoice_payload,
                currency=query.currency,
                total_amount=query.total_amount,
            )
            and product.is_active
            and product.price_xtr == order.amount_xtr
            and bool(product.telegram_file_id)
            and age_valid
        )
    if valid:
        await query.answer(ok=True)
    else:
        await query.answer(
            ok=False,
            error_message="The product or price changed. Please return to the store and try again.",
        )


async def successful_payment_handler(
    update: Update, context: ContextTypes.DEFAULT_TYPE
) -> None:
    message = update.effective_message
    user = update.effective_user
    payment = message.successful_payment if message else None
    if payment is None or user is None:
        return

    order_id: int | None = None
    async with session_scope() as session:
        result = await session.execute(
            select(Order)
            .options(selectinload(Order.user))
            .where(Order.invoice_payload == payment.invoice_payload)
        )
        order = result.scalar_one_or_none()
        if order is None:
            logger.error("Confirmed Telegram payment has unknown payload %s", payment.invoice_payload)
            await message.reply_text("Payment received, but the order needs support attention.")
            return
        if order.status == "paid":
            if order.telegram_payment_charge_id == payment.telegram_payment_charge_id:
                await message.reply_text("This purchase has already been processed.")
            else:
                logger.error("Unexpected second charge for order %s", order.id)
                await message.reply_text("Payment received, but the order needs support attention.")
            return
        details_valid = (
            order.status == "pending"
            and order.user.telegram_id == user.id
            and order.invoice_payload == payment.invoice_payload
            and order.currency == payment.currency == "XTR"
            and order.amount_xtr == payment.total_amount
        )
        duplicate_charge = await session.scalar(
            select(Order.id).where(
                Order.telegram_payment_charge_id == payment.telegram_payment_charge_id
            )
        )
        if not details_valid or duplicate_charge is not None:
            logger.error("Rejected payment details for order %s", order.id)
            await message.reply_text("Payment received, but the order needs support attention.")
            return
        order.status = "paid"
        order.telegram_payment_charge_id = payment.telegram_payment_charge_id
        order.paid_at = utc_now()
        order_id = order.id
        await session.commit()

    delivered = await deliver_order(context.bot, user.id, order_id)
    if delivered:
        await message.reply_text("Payment confirmed. Your file is attached above.")
    else:
        await message.reply_text(
            "Payment confirmed, but automatic delivery failed. Please open My Purchases to retry or contact support."
        )
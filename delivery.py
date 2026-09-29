import logging

from sqlalchemy import select, update
from telegram import Bot

from database import session_scope
from models import Order, Product, utc_now

logger = logging.getLogger(__name__)


async def deliver_order(bot: Bot, chat_id: int, order_id: int, *, resend: bool = False) -> bool:
    async with session_scope() as session:
        result = await session.execute(
            select(Order, Product)
            .join(Product, Order.product_id == Product.id)
            .where(Order.id == order_id)
        )
        row = result.one_or_none()
        if row is None:
            return False
        order, product = row
        if order.status != "paid" or not product.telegram_file_id:
            return False
        if order.delivery_status == "delivered" and not resend:
            return True
        claim = update(Order).where(Order.id == order_id, Order.status == "paid")
        if not resend:
            claim = claim.where(Order.delivery_status != "sending")
        claim = claim.values(delivery_status="sending")
        claimed = await session.execute(claim)
        if claimed.rowcount != 1:
            return False
        file_id = product.telegram_file_id
        caption = f"Your purchase: {product.name}"
        await session.commit()

    try:
        await bot.send_document(chat_id=chat_id, document=file_id, caption=caption)
    except Exception:
        logger.exception("Delivery failed for order %s", order_id)
        async with session_scope() as session:
            await session.execute(
                update(Order).where(Order.id == order_id).values(delivery_status="failed")
            )
            await session.commit()
        return False

    async with session_scope() as session:
        await session.execute(
            update(Order)
            .where(Order.id == order_id)
            .values(delivery_status="delivered", delivered_at=utc_now())
        )
        await session.commit()
    return True
from telegram import InlineKeyboardButton, InlineKeyboardMarkup, ReplyKeyboardMarkup


def main_menu() -> ReplyKeyboardMarkup:
    return ReplyKeyboardMarkup(
        [["🛍️ Store", "📦 My Purchases"], ["💬 Support"]],
        resize_keyboard=True,
        is_persistent=True,
        input_field_placeholder="Choose an option",
    )


def product_list(products: list[tuple[int, str, int]]) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton(
                    f"✨ {name} • {price} XTR",
                    callback_data=f"product:{product_id}",
                )
            ]
            for product_id, name, price in products
        ]
    )


def product_detail(product_id: int) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(
        [
            [InlineKeyboardButton("💳 Buy with XTR", callback_data=f"buy:{product_id}")],
            [InlineKeyboardButton("⬅️ Back to Store", callback_data="products:list")],
        ]
    )


def purchase_list(orders: list[tuple[int, str, str]]) -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton(
                    f"📦 {name} • {status}",
                    callback_data=f"purchase:{order_id}",
                )
            ]
            for order_id, name, status in orders
        ]
    )
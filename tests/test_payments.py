from types import SimpleNamespace

from payments import valid_payment_details


def test_payment_details_require_matching_user_payload_currency_and_amount() -> None:
    order = SimpleNamespace(
        status="pending",
        user=SimpleNamespace(telegram_id=123),
        invoice_payload="order:abc",
        currency="XTR",
        amount_xtr=25,
    )
    base = {
        "order": order,
        "telegram_user_id": 123,
        "payload": "order:abc",
        "currency": "XTR",
        "total_amount": 25,
    }

    assert valid_payment_details(**base)
    assert not valid_payment_details(**{**base, "telegram_user_id": 999})
    assert not valid_payment_details(**{**base, "payload": "order:other"})
    assert not valid_payment_details(**{**base, "currency": "USD"})
    assert not valid_payment_details(**{**base, "total_amount": 24})
    paid_order = SimpleNamespace(**{**order.__dict__, "status": "paid"})
    assert not valid_payment_details(**{**base, "order": paid_order})
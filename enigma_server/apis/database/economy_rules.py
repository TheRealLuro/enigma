from __future__ import annotations

import math
from decimal import Decimal
from typing import Any

from bson.decimal128 import Decimal128

from .founders_mark import (
    FOUNDERS_MARK_FEE_REDUCTION,
    FOUNDERS_MARK_REWARD_MULTIPLIER,
    has_founders_mark,
)
from .system_accounts import ensure_bank_account
from .user_utils import SYSTEM_BANK_USERNAME


SINGLE_PLAYER_BANK_FEE_RATE = 0.05
MULTIPLAYER_BANK_FEE_RATE = 0.10
MARKETPLACE_BANK_FEE_RATE = 0.10
LOSS_FEE_MN = 50


def compute_single_player_reward(gross_reward: int, user: dict[str, Any]) -> dict[str, Any]:
    gross = max(0, int(gross_reward or 0))
    base_fee = math.ceil(gross * SINGLE_PLAYER_BANK_FEE_RATE)
    fee_rebate = math.ceil(base_fee * FOUNDERS_MARK_FEE_REDUCTION) if has_founders_mark(user) else 0
    bank_dividend = max(0, base_fee - fee_rebate)
    base_reward = max(0, gross - base_fee + fee_rebate)
    reward_multiplier = FOUNDERS_MARK_REWARD_MULTIPLIER if has_founders_mark(user) else 1.0
    rewarded_mn = int(math.ceil(base_reward * reward_multiplier))

    return {
        "gross_reward": gross,
        "base_fee": base_fee,
        "fee_rebate": fee_rebate,
        "bank_dividend": bank_dividend,
        "base_reward": base_reward,
        "reward_multiplier": reward_multiplier,
        "rewarded_mn": rewarded_mn,
    }


def compute_multiplayer_rewards(
    total_rewards: int,
    owner_user: dict[str, Any],
    guest_user: dict[str, Any],
) -> dict[str, Any]:
    gross_total = max(0, int(total_rewards or 0))
    base_bank_dividend = math.ceil(gross_total * MULTIPLAYER_BANK_FEE_RATE)
    distributable = max(0, gross_total - base_bank_dividend)

    owner_share = math.ceil(distributable / 2)
    guest_share = max(0, distributable - owner_share)

    owner_fee_share = math.ceil(base_bank_dividend / 2)
    guest_fee_share = max(0, base_bank_dividend - owner_fee_share)

    owner_rebate = math.ceil(owner_fee_share * FOUNDERS_MARK_FEE_REDUCTION) if has_founders_mark(owner_user) else 0
    guest_rebate = math.ceil(guest_fee_share * FOUNDERS_MARK_FEE_REDUCTION) if has_founders_mark(guest_user) else 0

    owner_base_reward = owner_share + owner_rebate
    guest_base_reward = guest_share + guest_rebate

    owner_multiplier = FOUNDERS_MARK_REWARD_MULTIPLIER if has_founders_mark(owner_user) else 1.0
    guest_multiplier = FOUNDERS_MARK_REWARD_MULTIPLIER if has_founders_mark(guest_user) else 1.0

    owner_reward = int(math.ceil(owner_base_reward * owner_multiplier))
    guest_reward = int(math.ceil(guest_base_reward * guest_multiplier))
    bank_dividend = max(0, base_bank_dividend - owner_rebate - guest_rebate)

    return {
        "gross_total": gross_total,
        "base_bank_dividend": base_bank_dividend,
        "bank_dividend": bank_dividend,
        "owner": {
            "base_share": owner_share,
            "fee_share": owner_fee_share,
            "fee_rebate": owner_rebate,
            "reward_multiplier": owner_multiplier,
            "rewarded_mn": owner_reward,
        },
        "guest": {
            "base_share": guest_share,
            "fee_share": guest_fee_share,
            "fee_rebate": guest_rebate,
            "reward_multiplier": guest_multiplier,
            "rewarded_mn": guest_reward,
        },
    }


def compute_marketplace_sale_split(price: int) -> dict[str, int]:
    gross_price = max(0, int(price or 0))
    bank_dividend = int(math.ceil(gross_price * MARKETPLACE_BANK_FEE_RATE))
    seller_reward = max(0, gross_price - bank_dividend)
    return {
        "gross_price": gross_price,
        "bank_dividend": bank_dividend,
        "seller_reward": seller_reward,
    }


def compute_loss_fee(user: dict[str, Any]) -> dict[str, int]:
    raw_balance = user.get("maze_nuggets", 0)
    if isinstance(raw_balance, Decimal128):
        try:
            balance = int(raw_balance.to_decimal())
        except (ArithmeticError, ValueError):
            balance = 0
    elif isinstance(raw_balance, Decimal):
        try:
            balance = int(raw_balance)
        except (ArithmeticError, ValueError):
            balance = 0
    else:
        try:
            balance = int(raw_balance or 0)
        except (TypeError, ValueError):
            balance = 0

    balance = max(0, balance)
    applied_fee = min(balance, LOSS_FEE_MN)
    return {
        "base_fee": LOSS_FEE_MN,
        "applied_fee": applied_fee,
    }


def credit_bank_dividend(users_collection, amount: int, mongo_session=None) -> None:
    dividend = max(0, int(amount or 0))
    if dividend <= 0:
        return

    ensure_bank_account()
    users_collection.update_one(
        {"username": SYSTEM_BANK_USERNAME},
        {"$inc": {"maze_nuggets": Decimal128(str(dividend))}},
        session=mongo_session,
    )

from __future__ import annotations

import re
from typing import Any

from fastapi import HTTPException

USERNAME_MIN_LENGTH = 3
USERNAME_MAX_LENGTH = 32
PASSWORD_MIN_LENGTH = 10
PASSWORD_MAX_LENGTH = 128
EMAIL_MAX_LENGTH = 254

_CONTROL_CHAR_PATTERN = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")
_EMAIL_PATTERN = re.compile(
    r"(?i)^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@"
    r"[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?"
    r"(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+$"
)
_USERNAME_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{2,31}$")
_SQL_TAUTOLOGY_PATTERN = re.compile(r"(?i)(?:'|\")\s*or\s*(?:'?\d+'?\s*=\s*'?\d+'?)")
_DANGEROUS_SUBSTRINGS = (
    "<script",
    "</script",
    "javascript:",
    "vbscript:",
    "onerror=",
    "onload=",
    "document.cookie",
    "${jndi:",
    "db.eval(",
    "$where",
    ";--",
)


def _field_label(field_name: str) -> str:
    return field_name.replace("_", " ").strip().capitalize() or "Field"


def ensure_safe_text(
    value: Any,
    *,
    field_name: str,
    required: bool = True,
    min_length: int | None = None,
    max_length: int | None = None,
) -> str:
    text = str(value or "").strip()
    label = _field_label(field_name)

    if required and not text:
        raise HTTPException(status_code=400, detail=f"{label} is required")

    if not text:
        return text

    if min_length is not None and len(text) < int(min_length):
        raise HTTPException(status_code=400, detail=f"{label} must be at least {int(min_length)} characters")
    if max_length is not None and len(text) > int(max_length):
        raise HTTPException(status_code=400, detail=f"{label} must be at most {int(max_length)} characters")

    if _CONTROL_CHAR_PATTERN.search(text):
        raise HTTPException(status_code=400, detail=f"{label} contains unsupported control characters")

    lowered = text.casefold()
    if any(token in lowered for token in _DANGEROUS_SUBSTRINGS) or _SQL_TAUTOLOGY_PATTERN.search(text):
        raise HTTPException(status_code=400, detail=f"{label} contains disallowed content")

    return text


def validate_username(username: Any, *, field_name: str = "username") -> str:
    normalized = ensure_safe_text(
        username,
        field_name=field_name,
        min_length=USERNAME_MIN_LENGTH,
        max_length=USERNAME_MAX_LENGTH,
    )
    if not _USERNAME_PATTERN.fullmatch(normalized):
        raise HTTPException(
            status_code=400,
            detail=(
                f"{_field_label(field_name)} can only use letters, numbers, dot, underscore, and hyphen; "
                "it must start with a letter or number"
            ),
        )
    return normalized


def validate_login_username(username: Any, *, field_name: str = "username") -> str:
    # Keep login compatibility with legacy usernames while still blocking dangerous payloads.
    return ensure_safe_text(username, field_name=field_name, min_length=1, max_length=64)


def validate_email_address(email: Any, *, field_name: str = "email") -> str:
    normalized = ensure_safe_text(email, field_name=field_name, min_length=3, max_length=EMAIL_MAX_LENGTH)
    if len(normalized) > EMAIL_MAX_LENGTH:
        raise HTTPException(status_code=400, detail=f"{_field_label(field_name)} is too long")
    if not _EMAIL_PATTERN.fullmatch(normalized):
        raise HTTPException(status_code=400, detail=f"{_field_label(field_name)} must be a valid email address")

    local_part, _, domain_part = normalized.partition("@")
    if len(local_part) > 64 or not domain_part:
        raise HTTPException(status_code=400, detail=f"{_field_label(field_name)} must be a valid email address")

    return normalized


def validate_password_strength(password: Any, *, field_name: str = "password") -> str:
    normalized = str(password or "")
    label = _field_label(field_name)

    if not normalized:
        raise HTTPException(status_code=400, detail=f"{label} is required")
    if len(normalized) < PASSWORD_MIN_LENGTH:
        raise HTTPException(
            status_code=400,
            detail=f"{label} must be at least {PASSWORD_MIN_LENGTH} characters",
        )
    if len(normalized) > PASSWORD_MAX_LENGTH:
        raise HTTPException(
            status_code=400,
            detail=f"{label} must be at most {PASSWORD_MAX_LENGTH} characters",
        )
    if _CONTROL_CHAR_PATTERN.search(normalized):
        raise HTTPException(status_code=400, detail=f"{label} contains unsupported characters")
    if not re.search(r"[A-Z]", normalized):
        raise HTTPException(status_code=400, detail=f"{label} must include at least one uppercase letter")
    if not re.search(r"[a-z]", normalized):
        raise HTTPException(status_code=400, detail=f"{label} must include at least one lowercase letter")
    if not re.search(r"\d", normalized):
        raise HTTPException(status_code=400, detail=f"{label} must include at least one number")
    if not re.search(r"[^A-Za-z0-9]", normalized):
        raise HTTPException(status_code=400, detail=f"{label} must include at least one special character")

    return normalized


def sanitize_request_string(value: str) -> str | None:
    text = str(value or "")
    if not text:
        return None
    if _CONTROL_CHAR_PATTERN.search(text):
        return "contains control characters"
    lowered = text.casefold()
    if any(token in lowered for token in _DANGEROUS_SUBSTRINGS):
        return "contains disallowed content"
    if _SQL_TAUTOLOGY_PATTERN.search(text):
        return "contains SQL-injection-like content"
    return None

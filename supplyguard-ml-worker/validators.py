"""Canonical UUID and UTC timestamp validation shared by wire contracts."""

import re
from datetime import UTC, datetime, timedelta
from uuid import UUID


def parse_uuid(value: object) -> UUID:
    if not isinstance(value, (str, UUID)):
        raise ValueError("Expected a canonical UUID.")
    parsed = UUID(value) if isinstance(value, str) else value
    if parsed.int == 0 or (isinstance(value, str) and str(parsed) != value):
        raise ValueError("Expected a non-empty, lowercase, hyphenated UUID.")
    return parsed


def parse_timestamp(value: object) -> datetime:
    if isinstance(value, str):
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z", value):
            raise ValueError("Expected UTC ISO 8601 with milliseconds and Z.")
        value = datetime.fromisoformat(value)
    if not isinstance(value, datetime) or value.utcoffset() != timedelta(0):
        raise ValueError("Expected a timezone-aware UTC datetime.")
    return value


def format_timestamp(value: datetime) -> str:
    return value.astimezone(UTC).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def utc_now() -> datetime:
    value = datetime.now(UTC)
    return value.replace(microsecond=value.microsecond // 1000 * 1000)

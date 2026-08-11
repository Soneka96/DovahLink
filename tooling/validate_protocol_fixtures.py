#!/usr/bin/env python3
"""Validate every protocol/fixtures/**/*.json file against the v1 envelope contract
documented in protocol/schema/README.md.

This checks envelope shape only (required fields, types, and the sessionId
null-ability rule for hello and pre-session error messages). Per-message-type
payload validation belongs to the registered message codecs consuming these
fixtures on the bridge and client sides.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

FIXTURES_DIR = Path(__file__).resolve().parent.parent / "protocol" / "fixtures"

REQUIRED_FIELDS = (
    "protocolVersion",
    "messageType",
    "messageId",
    "sessionId",
    "correlationId",
    "payload",
)


class FixtureError(ValueError):
    pass


def validate_envelope(name: str, message: object) -> None:
    if not isinstance(message, dict):
        raise FixtureError(f"{name}: top-level JSON value must be an object")

    missing = [field for field in REQUIRED_FIELDS if field not in message]
    if missing:
        raise FixtureError(f"{name}: missing required field(s): {', '.join(missing)}")

    protocol_version = message["protocolVersion"]
    if not isinstance(protocol_version, int) or isinstance(protocol_version, bool) or protocol_version < 0:
        raise FixtureError(f"{name}: protocolVersion must be a non-negative integer")

    message_type = message["messageType"]
    if not isinstance(message_type, str) or not message_type:
        raise FixtureError(f"{name}: messageType must be a non-empty string")

    message_id = message["messageId"]
    if not isinstance(message_id, str) or not message_id:
        raise FixtureError(f"{name}: messageId must be a non-empty string")

    correlation_id = message["correlationId"]
    if correlation_id is not None and not isinstance(correlation_id, str):
        raise FixtureError(f"{name}: correlationId must be a string or null")

    # sessionId is null for pre-authentication hello, and may be null for an error
    # that rejects a connection before a session exists (protocol/schema/README.md).
    session_id = message["sessionId"]
    if message_type == "hello":
        if session_id is not None:
            raise FixtureError(f"{name}: sessionId must be null for hello, since it precedes authentication")
    elif message_type == "error":
        if session_id is not None and (not isinstance(session_id, str) or not session_id):
            raise FixtureError(f"{name}: sessionId must be null or a non-empty string for 'error'")
    elif not isinstance(session_id, str) or not session_id:
        raise FixtureError(f"{name}: sessionId must be a non-empty string for '{message_type}'")

    payload = message["payload"]
    if not isinstance(payload, dict):
        raise FixtureError(f"{name}: payload must be an object")


def validate_all(fixtures_dir: Path = FIXTURES_DIR) -> list[str]:
    fixture_files = sorted(fixtures_dir.rglob("*.json"))
    if not fixture_files:
        raise FixtureError(f"no fixture files found in {fixtures_dir}")

    checked = []
    for path in fixture_files:
        with path.open(encoding="utf-8") as f:
            message = json.load(f)
        relative_name = path.relative_to(fixtures_dir).as_posix()
        validate_envelope(relative_name, message)
        checked.append(relative_name)
    return checked


def main() -> int:
    try:
        checked = validate_all()
    except (FixtureError, json.JSONDecodeError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    print(f"OK: {len(checked)} fixture(s) satisfy the envelope contract")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

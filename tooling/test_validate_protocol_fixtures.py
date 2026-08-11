import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from validate_protocol_fixtures import FIXTURES_DIR, FixtureError, main, validate_all, validate_envelope


def _valid_hello() -> dict:
    return {
        "protocolVersion": 0,
        "messageType": "hello",
        "messageId": "m-1",
        "sessionId": None,
        "correlationId": None,
        "payload": {},
    }


def _valid_snapshot() -> dict:
    return {
        "protocolVersion": 1,
        "messageType": "state_snapshot",
        "messageId": "m-2",
        "sessionId": "session-1",
        "correlationId": "m-1",
        "payload": {},
    }


def _valid_error(session_id: str | None) -> dict:
    return {
        "protocolVersion": 0,
        "messageType": "error",
        "messageId": "m-3",
        "sessionId": session_id,
        "correlationId": "m-1",
        "payload": {"code": "unauthenticated", "message": "boom", "retryable": False, "details": None},
    }


class ValidateEnvelopeTests(unittest.TestCase):
    def test_valid_hello_passes(self) -> None:
        validate_envelope("hello.json", _valid_hello())

    def test_valid_non_hello_passes(self) -> None:
        validate_envelope("snapshot.json", _valid_snapshot())

    def test_missing_field_rejected(self) -> None:
        message = _valid_snapshot()
        del message["payload"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_object_top_level_rejected(self) -> None:
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", [])

    def test_negative_protocol_version_rejected(self) -> None:
        message = _valid_snapshot()
        message["protocolVersion"] = -1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_integer_protocol_version_rejected(self) -> None:
        message = _valid_snapshot()
        message["protocolVersion"] = "1"
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_empty_message_type_rejected(self) -> None:
        message = _valid_snapshot()
        message["messageType"] = ""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_correlation_id_rejected(self) -> None:
        message = _valid_snapshot()
        message["correlationId"] = 5
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_hello_with_non_null_session_id_rejected(self) -> None:
        message = _valid_hello()
        message["sessionId"] = "session-1"
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_hello_with_null_session_id_rejected(self) -> None:
        message = _valid_snapshot()
        message["sessionId"] = None
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_object_payload_rejected(self) -> None:
        message = _valid_snapshot()
        message["payload"] = "not an object"
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_bool_protocol_version_rejected(self) -> None:
        # bool is a subclass of int in Python; must not slip past isinstance(int).
        message = _valid_snapshot()
        message["protocolVersion"] = True
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_message_type_rejected(self) -> None:
        message = _valid_snapshot()
        message["messageType"] = 7
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_missing_message_id_rejected(self) -> None:
        message = _valid_snapshot()
        del message["messageId"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_empty_message_id_rejected(self) -> None:
        message = _valid_snapshot()
        message["messageId"] = ""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_empty_session_id_for_non_hello_rejected(self) -> None:
        message = _valid_snapshot()
        message["sessionId"] = ""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_session_id_for_non_hello_rejected(self) -> None:
        message = _valid_snapshot()
        message["sessionId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_error_with_null_session_id_passes(self) -> None:
        validate_envelope("error.json", _valid_error(None))

    def test_error_with_session_id_passes(self) -> None:
        validate_envelope("error.json", _valid_error("session-1"))

    def test_error_with_empty_session_id_rejected(self) -> None:
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", _valid_error(""))

    def test_error_with_non_string_session_id_rejected(self) -> None:
        message = _valid_error(None)
        message["sessionId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)


class ValidateAllTests(unittest.TestCase):
    def test_real_fixtures_directory_passes(self) -> None:
        checked = validate_all(FIXTURES_DIR)
        expected = {
            "connection/hello.json",
            "connection/hello-ack.json",
            "capabilities/capabilities-bridge.json",
            "capabilities/capabilities-client.json",
            "subscriptions/subscribe.json",
            "subscriptions/subscription-ack.json",
            "state/character/character-state-event.json",
            "state/character/character-state-snapshot.json",
            "state/character/character-state-unavailable.json",
            "errors/error-frame-too-large.json",
            "errors/error-malformed-message.json",
            "errors/error-rate-limited.json",
            "errors/error-replayed-message.json",
            "errors/error-stale-session.json",
            "errors/error-unauthenticated-expired-token.json",
            "errors/error-unauthenticated-invalid-token.json",
            "errors/error-unauthenticated-reused-token.json",
            "errors/error-unsupported-version.json",
            "subscriptions/snapshot-request.json",
            "state/character/state-event-revision-gap.json",
            "state/character/state-event-duplicate.json",
            "state/character/state-event-stale.json",
            "state/character/state-snapshot-unknown-field.json",
            "connection/ping.json",
            "connection/pong.json",
        }
        self.assertEqual(set(checked), expected)

    def test_nested_fixture_is_discovered_with_relative_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "state" / "character" / "snapshot.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps(_valid_snapshot()), encoding="utf-8")

            self.assertEqual(validate_all(Path(tmp)), ["state/character/snapshot.json"])

    def test_empty_directory_raises(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(FixtureError):
                validate_all(Path(tmp))

    def test_malformed_json_propagates(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "errors" / "broken.json"
            path.parent.mkdir()
            path.write_text("{not json", encoding="utf-8")
            with self.assertRaises(json.JSONDecodeError):
                validate_all(Path(tmp))

    def test_envelope_invalid_fixture_propagates(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "errors" / "bad-envelope.json"
            path.parent.mkdir()
            path.write_text(json.dumps({"messageType": "hello"}), encoding="utf-8")
            with self.assertRaises(FixtureError):
                validate_all(Path(tmp))


class MainTests(unittest.TestCase):
    def test_main_returns_zero_for_real_fixtures(self) -> None:
        self.assertEqual(main(), 0)

    def test_main_returns_one_on_failure(self) -> None:
        with patch("validate_protocol_fixtures.validate_all", side_effect=FixtureError("boom")):
            self.assertEqual(main(), 1)

    def test_main_returns_one_on_malformed_json(self) -> None:
        error = json.JSONDecodeError("broken", "{", 0)
        with patch("validate_protocol_fixtures.validate_all", side_effect=error):
            self.assertEqual(main(), 1)


if __name__ == "__main__":
    unittest.main()

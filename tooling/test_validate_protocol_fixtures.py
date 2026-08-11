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


class ValidateAllTests(unittest.TestCase):
    def test_real_fixtures_directory_passes(self) -> None:
        checked = validate_all(FIXTURES_DIR)
        self.assertIn("hello.json", checked)
        self.assertIn("hello-ack.json", checked)
        self.assertIn("capabilities-bridge.json", checked)
        self.assertIn("capabilities-client.json", checked)
        self.assertIn("subscribe.json", checked)
        self.assertIn("subscription-ack.json", checked)
        self.assertIn("character-state-snapshot.json", checked)

    def test_empty_directory_raises(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(FixtureError):
                validate_all(Path(tmp))

    def test_malformed_json_propagates(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "broken.json"
            path.write_text("{not json", encoding="utf-8")
            with self.assertRaises(json.JSONDecodeError):
                validate_all(Path(tmp))

    def test_envelope_invalid_fixture_propagates(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "bad-envelope.json"
            path.write_text(json.dumps({"messageType": "hello"}), encoding="utf-8")
            with self.assertRaises(FixtureError):
                validate_all(Path(tmp))


class MainTests(unittest.TestCase):
    def test_main_returns_zero_for_real_fixtures(self) -> None:
        self.assertEqual(main(), 0)

    def test_main_returns_one_on_failure(self) -> None:
        with patch("validate_protocol_fixtures.validate_all", side_effect=FixtureError("boom")):
            self.assertEqual(main(), 1)


if __name__ == "__main__":
    unittest.main()

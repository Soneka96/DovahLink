"""Test protocol fixture envelope validation and command-line reporting."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from validate_protocol_fixtures import (
    FIXTURES_DIR,
    FixtureError,
    main,
    validate_all,
    validate_envelope,
)


def _valid_hello() -> dict:
    """Create a valid hello message envelope for use in tests."""
    return {
        "messageType": "hello",
        "messageId": "m-1",
        "sessionId": None,
        "correlationId": None,
        "payload": {},
        "bridgeInstanceId": None,
        "playContextId": None,
        "clientId": None,
    }


def _valid_snapshot() -> dict:
    """Create a valid state snapshot envelope for testing."""
    return {
        "messageType": "state_snapshot",
        "messageId": "m-2",
        "sessionId": "session-1",
        "correlationId": "m-1",
        "payload": {},
        "bridgeInstanceId": "bridge-1",
        "playContextId": None,
        "clientId": "client-1",
    }


def _valid_error(session_id: str | None) -> dict:
    """Create a valid error message envelope for the specified session.

    Args:
        session_id: The session identifier to include in the envelope.

    Returns:
        A protocol-compliant error envelope.
    """
    return {
        "messageType": "error",
        "messageId": "m-3",
        "sessionId": session_id,
        "correlationId": "m-1",
        "payload": {
            "code": "unauthenticated",
            "message": "boom",
            "retryable": False,
            "details": None,
        },
        "bridgeInstanceId": None,
        "playContextId": None,
        "clientId": None,
    }


class ValidateEnvelopeTests(unittest.TestCase):
    """Verify validation of individual protocol envelopes."""

    def test_valid_hello_passes(self) -> None:
        """Accept a valid hello envelope."""
        validate_envelope("hello.json", _valid_hello())

    def test_valid_non_hello_passes(self) -> None:
        """Accept a valid non-hello envelope."""
        validate_envelope("snapshot.json", _valid_snapshot())

    def test_valid_snapshot_with_active_play_context_passes(self) -> None:
        """Accept a non-null playContextId identifying an active play context."""
        message = _valid_snapshot()
        message["playContextId"] = "context-1"
        validate_envelope("snapshot.json", message)

    def test_missing_field_rejected(self) -> None:
        """Reject an envelope missing a required field."""
        message = _valid_snapshot()
        del message["payload"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_missing_bridge_instance_id_rejected(self) -> None:
        """Reject an envelope missing the bridgeInstanceId key entirely."""
        message = _valid_snapshot()
        del message["bridgeInstanceId"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_missing_play_context_id_rejected(self) -> None:
        """Reject an envelope missing the playContextId key entirely."""
        message = _valid_snapshot()
        del message["playContextId"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_missing_client_id_rejected(self) -> None:
        """Reject an envelope missing the clientId key entirely."""
        message = _valid_snapshot()
        del message["clientId"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_object_top_level_rejected(self) -> None:
        """Reject a non-object top-level JSON value."""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", [])

    def test_non_string_bridge_instance_id_rejected(self) -> None:
        """Reject a bridgeInstanceId with the wrong type."""
        message = _valid_snapshot()
        message["bridgeInstanceId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_play_context_id_rejected(self) -> None:
        """Reject a playContextId with the wrong type."""
        message = _valid_snapshot()
        message["playContextId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_client_id_rejected(self) -> None:
        """Reject a clientId with the wrong type."""
        message = _valid_snapshot()
        message["clientId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_empty_message_type_rejected(self) -> None:
        """Reject an empty message type."""
        message = _valid_snapshot()
        message["messageType"] = ""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_correlation_id_rejected(self) -> None:
        """Reject a correlation identifier with the wrong type."""
        message = _valid_snapshot()
        message["correlationId"] = 5
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_hello_with_non_null_session_id_rejected(self) -> None:
        """Reject a hello envelope with a session identifier."""
        message = _valid_hello()
        message["sessionId"] = "session-1"
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_hello_with_null_session_id_rejected(self) -> None:
        """Reject a non-hello envelope without a session identifier."""
        message = _valid_snapshot()
        message["sessionId"] = None
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_object_payload_rejected(self) -> None:
        """Reject an envelope whose payload is not an object."""
        message = _valid_snapshot()
        message["payload"] = "not an object"
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_message_type_rejected(self) -> None:
        """Reject a message type with the wrong type."""
        message = _valid_snapshot()
        message["messageType"] = 7
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_missing_message_id_rejected(self) -> None:
        """Reject an envelope missing its message identifier."""
        message = _valid_snapshot()
        del message["messageId"]
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_empty_message_id_rejected(self) -> None:
        """Reject an envelope with an empty message identifier."""
        message = _valid_snapshot()
        message["messageId"] = ""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_empty_session_id_for_non_hello_rejected(self) -> None:
        """Reject an empty session identifier for a non-hello message."""
        message = _valid_snapshot()
        message["sessionId"] = ""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_non_string_session_id_for_non_hello_rejected(self) -> None:
        """Reject a non-string session identifier for a non-hello message."""
        message = _valid_snapshot()
        message["sessionId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)

    def test_error_with_null_session_id_passes(self) -> None:
        """Accept an error envelope without a session identifier."""
        validate_envelope("error.json", _valid_error(None))

    def test_error_with_session_id_passes(self) -> None:
        """Accept an error envelope with a session identifier."""
        validate_envelope("error.json", _valid_error("session-1"))

    def test_error_with_empty_session_id_rejected(self) -> None:
        """Reject an error envelope with an empty session identifier."""
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", _valid_error(""))

    def test_error_with_non_string_session_id_rejected(self) -> None:
        """Reject an error envelope with a non-string session identifier."""
        message = _valid_error(None)
        message["sessionId"] = 1
        with self.assertRaises(FixtureError):
            validate_envelope("bad.json", message)


class ValidateAllTests(unittest.TestCase):
    """Verify validation of fixture directories."""

    def test_real_fixtures_directory_passes(self) -> None:
        """Validate the repository's protocol fixtures."""
        checked = validate_all(FIXTURES_DIR)
        expected = {
            "connection/hello.json",
            "connection/hello-ack.json",
            "connection/hello-ack-paired.json",
            "connection/hello-ack-active-context.json",
            "connection/hello-unpaired.json",
            "connection/hello-trusted-device-credential.json",
            "pairing/pairing-request.json",
            "pairing/pairing-status-unavailable.json",
            "pairing/pairing-status-available.json",
            "pairing/pairing-status-in-progress.json",
            "pairing/pairing-status-other-device.json",
            "pairing/pairing-confirm.json",
            "pairing/pairing-ack.json",
            "pairing/pairing-cancel.json",
            "pairing/pairing-renotify.json",
            "pairing/pairing-outcome-credential-issued.json",
            "pairing/pairing-outcome-trusted.json",
            "pairing/pairing-outcome-already-trusted.json",
            "pairing/pairing-outcome-already-idle.json",
            "pairing/pairing-outcome-expired.json",
            "pairing/pairing-outcome-invalid.json",
            "pairing/pairing-outcome-pacing-limited.json",
            "pairing/pairing-outcome-pending-not-found.json",
            "pairing/pairing-outcome-invalidated.json",
            "pairing/pairing-outcome-renotified.json",
            "pairing/pairing-outcome-renotify-cooldown.json",
            "pairing/pairing-outcome-cancelled.json",
            "pairing/pairing-outcome-hard-limit-reached.json",
            "capabilities/capabilities-bridge.json",
            "capabilities/capabilities-client.json",
            "subscriptions/subscribe.json",
            "subscriptions/subscription-ack.json",
            "state/state-event.json",
            "state/state-snapshot.json",
            "state/state-snapshot-unavailable.json",
            "errors/error-blocked.json",
            "errors/error-frame-too-large.json",
            "errors/error-malformed-message.json",
            "errors/error-rate-limited.json",
            "errors/error-replayed-message.json",
            "errors/error-revoked.json",
            "errors/error-stale-session.json",
            "errors/error-unauthenticated-expired-token.json",
            "errors/error-unauthenticated-invalid-token.json",
            "errors/error-unauthenticated-reused-token.json",
            "errors/error-unsupported-capability.json",
            "subscriptions/snapshot-request.json",
            "state/state-event-revision-gap.json",
            "state/state-event-duplicate.json",
            "state/state-event-stale.json",
            "state/state-snapshot-unknown-field.json",
            "connection/ping.json",
            "connection/pong.json",
            "rename/rename-outcome-invalid-display-name.json",
            "rename/rename-outcome-not-trusted.json",
            "rename/rename-outcome-renamed.json",
            "rename/rename-request.json",
        }
        self.assertEqual(set(checked), expected)

    def test_nested_fixture_is_discovered_with_relative_path(self) -> None:
        """Discover nested fixtures and return relative paths."""
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "state" / "example_area" / "snapshot.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps(_valid_snapshot()), encoding="utf-8")

            self.assertEqual(
                validate_all(Path(tmp)), ["state/example_area/snapshot.json"]
            )

    def test_empty_directory_raises(self) -> None:
        """Reject a fixture directory that contains no JSON files."""
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(FixtureError):
                validate_all(Path(tmp))

    def test_malformed_json_propagates(self) -> None:
        """Propagate JSON parsing failures from fixture files."""
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "errors" / "broken.json"
            path.parent.mkdir()
            path.write_text("{not json", encoding="utf-8")
            with self.assertRaises(json.JSONDecodeError):
                validate_all(Path(tmp))

    def test_envelope_invalid_fixture_propagates(self) -> None:
        """Propagate envelope validation failures from fixture files."""
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "errors" / "bad-envelope.json"
            path.parent.mkdir()
            path.write_text(json.dumps({"messageType": "hello"}), encoding="utf-8")
            with self.assertRaises(FixtureError):
                validate_all(Path(tmp))


class MainTests(unittest.TestCase):
    """Verify the validator command-line entry point's return codes."""

    def test_main_returns_zero_for_real_fixtures(self) -> None:
        """Return zero when all repository fixtures validate."""
        self.assertEqual(main(), 0)

    def test_main_returns_one_on_failure(self) -> None:
        """Return one when fixture validation fails."""
        with patch(
            "validate_protocol_fixtures.validate_all", side_effect=FixtureError("boom")
        ):
            self.assertEqual(main(), 1)

    def test_main_returns_one_on_malformed_json(self) -> None:
        """Return one when fixture JSON is malformed."""
        error = json.JSONDecodeError("broken", "{", 0)
        with patch("validate_protocol_fixtures.validate_all", side_effect=error):
            self.assertEqual(main(), 1)


if __name__ == "__main__":
    unittest.main()

# Protocol schema v1

## Common envelope

Every message is one UTF-8 JSON object with these fields. The transport preserves one complete
object per message; framing is not part of the JSON payload.

```json
{
  "protocolVersion": 1,
  "messageType": "state_snapshot",
  "messageId": "opaque-message-id",
  "sessionId": "opaque-connection-id",
  "correlationId": null,
  "payload": {}
}
```

| Field | Type | Required | Meaning |
|---|---|---:|---|
| `protocolVersion` | non-negative integer | yes | Selected message version; `0` is reserved for pre-negotiation `hello` and `hello_ack`. |
| `messageType` | string | yes | Canonical message identifier. |
| `messageId` | string | yes | Cryptographically random and unique within the connection session; duplicate IDs are rejected. |
| `sessionId` | string or `null` | yes | `null` for pre-authentication `hello`, and `null` for an `error` that rejects a connection before any session was established on that socket (for example an auth failure or a violation detected before decoding completes). `hello_ack` and every other message carry the server-issued identity for that socket; an `error` reported after a session exists carries that session's identity. A session ID is valid only on the socket to which it was issued. |
| `correlationId` | string or `null` | yes | Message ID being answered, or `null` when there is no correlation; response rules are defined below. |
| `payload` | object | yes | Message-specific data. |

Unknown top-level fields are ignored only when the negotiated version permits forward-compatible reading. Required fields with the wrong type invalidate the message. Every message type below lists its required payload fields; fields not listed are not sent in v1.

## State envelope

`state_snapshot` and `state_event` payloads contain:

```json
{
  "stateArea": "character",
  "revision": 42,
  "occurredAt": "2026-08-10T12:00:00Z",
  "data": {}
}
```

- `stateArea` is a canonical identifier such as `character` or `location`.
- `revision` is a non-negative integer, monotonically increasing within one state area and session.
- Revisions are scoped to one state area and session. They reset when `sessionId` changes and are
  never compared across sessions.
- `occurredAt` is UTC RFC 3339 wall-clock time for display and diagnostics; it is not an ordering source.
- `data` contains the state-area contract.
- An unavailable value is represented explicitly as `null` or by the state-area's documented availability field; it must not be replaced with a plausible default.

An event additionally contains `baseRevision`, the revision the event expects the client to have before applying it:

```json
{
  "stateArea": "character",
  "baseRevision": 41,
  "revision": 42,
  "occurredAt": "2026-08-10T12:00:00Z",
  "data": {}
}
```

In v1, `data` is the complete post-change state for the state area, not a patch. `revision` must equal `baseRevision + 1`. If an event's `revision` is at or below the client's current revision, the client ignores it as a duplicate or stale message. If its `revision` is higher than the current revision and `baseRevision` does not equal the current revision, the client marks the state area stale and requests a fresh snapshot.

An accepted snapshot becomes the baseline for its state area and supersedes older events for that
area.

## Message types

### `hello`

Negotiates the connection before any optional state messages. The connecting client always sends
`hello` first; the bridge never initiates a connection or sends `hello` itself, and only replies
with `hello_ack` after it receives and validates one.

The envelope uses `protocolVersion: 0` because no version has been selected yet. The payload fields are:

```json
{
  "endpoint": "client",
  "supportedProtocolVersions": [1],
  "auth": {
    "method": "one_time_local_token",
    "token": "redacted-in-documentation"
  }
}
```

`endpoint` identifies the sender's role and is always `client` in v1, because only the connecting
client sends `hello`.

Required payload fields: `endpoint`, `supportedProtocolVersions`, `auth`. v1 accepts only `auth.method: one_time_local_token` during the loopback proof. The peer responds with `hello_ack` only after token validation.

### `hello_ack`

Uses `protocolVersion: 0`, carries the newly issued non-null `sessionId` in its envelope, and selects one common version:

```json
{
  "selectedProtocolVersion": 1
}
```

Required payload field: `selectedProtocolVersion`. All messages after this acknowledgement use the selected version and the server-issued session identity.

`hello_ack.correlationId` is the `messageId` of the `hello` it answers.

### `capabilities`

Declares supported features after version negotiation.

```json
{
  "capabilities": [
    {"id": "state.character", "version": 1}
  ]
}
```

Capability IDs and versions are canonical protocol values. A missing capability means the feature is unavailable and the client must remain usable without it.

Required payload field: `capabilities`. Each capability requires `id` and `version`.

Both endpoints send `capabilities`. In v1, the only registered capability is `state.character` version `1`; unregistered capability IDs are rejected. The bridge may advertise `state.character`; the client sends an empty capability list because no client-side capability IDs are defined yet.

### `subscribe`

Requests state areas after capabilities are negotiated.

```json
{
  "stateAreas": ["character"]
}
```

The bridge confirms the subscription and sends a `state_snapshot` before sending events for that state area.

Required payload field: `stateAreas`. The bridge responds with `subscription_ack`.

### `subscription_ack`

Confirms accepted and rejected state areas:

```json
{
  "acceptedStateAreas": ["character"],
  "rejectedStateAreas": []
}
```

Both arrays are required. The bridge sends snapshots only for accepted areas.

`subscription_ack.correlationId` is the `messageId` of the `subscribe` it answers. Each initial snapshot also uses the `subscribe` message ID as its `correlationId`; later snapshots use the `snapshot_request` message ID that caused them.

### `snapshot_request`

Requests a fresh baseline for one state area. The bridge responds with a `state_snapshot` at the current revision.

```json
{
  "stateArea": "character",
  "knownRevision": 41
}
```

Required payload field: `stateArea`. `knownRevision` is optional and advisory only.

### `state_snapshot`

Contains the complete state for one subscribed state area at a revision.

Required payload fields: `stateArea`, `revision`, `occurredAt`, `data`.

An initial snapshot correlates to `subscribe`; a recovery snapshot correlates to `snapshot_request`.

### `state_event`

Contains one ordered update from `baseRevision` to `revision` for one subscribed state area.

Required payload fields: `stateArea`, `baseRevision`, `revision`, `occurredAt`, `data`. v1 events contain complete post-change state, not partial patches.

### `character` state area

The v1 `character` state area contains a complete read-only snapshot of the player's
current level and three resource pools:

```json
{
  "level": 12,
  "health": {"current": 180.0, "maximum": 220.0},
  "magicka": {"current": 90.0, "maximum": 120.0},
  "stamina": {"current": 140.0, "maximum": 160.0}
}
```

The `level`, `health`, `magicka`, and `stamina` fields are required in the
payload and may be `null` when the bridge cannot provide a trustworthy value.
When a resource is present, `current` and `maximum` are required finite JSON
numbers. Values are game units; the protocol does not infer percentages or
replace unavailable values with zeroes.

### `error`

Reports a structured failure without exposing infrastructure exceptions:

```json
{
  "code": "unsupported_version",
  "message": "No mutually supported protocol version",
  "retryable": false,
  "details": null
}
```

`code` is a canonical machine-readable value. `message` is diagnostic text and must not be used for branching.

Required payload fields: `code`, `message`, `retryable`. `details` is nullable and optional when no safe diagnostic details exist.

Canonical v1 error codes include `malformed_message`, `frame_too_large`, `unsupported_version`, `unsupported_capability`, `unauthenticated`, `unauthorized`, `replayed_message`, `stale_session`, `rate_limited`, and `internal_error`. Error codes are for branching; diagnostic messages are not.

### `ping` and `pong`

Carry no application state. They prove liveness for the current `sessionId`.

`pong.correlationId` is the `messageId` of the `ping` it answers. `capabilities`, `state_event`, and unsolicited `error` messages use `correlationId: null`.

## Session and recovery rules

1. The connecting client sends `hello` using `protocolVersion: 0`.
2. The bridge selects the highest mutually supported protocol version and replies with `hello_ack`; if none exists, the connection ends with `unsupported_version`.
3. They exchange `capabilities` using the selected version.
4. The client sends `subscribe` and receives `subscription_ack`.
5. The bridge sends a snapshot before events for each accepted state area.
6. Each authenticated socket receives a unique `sessionId`. The session is bound exclusively to
   that socket and is invalidated when the socket closes for any reason.
7. A session cannot be transferred, resumed, or reused on another socket. A reconnect creates a
   new session, and messages carrying an invalidated or foreign session ID are rejected as
   `stale_session` before application handling.
8. The client must not apply messages from its previous session. Queued state from that session is
   not replayed; a fresh snapshot establishes each new
   baseline.
9. During snapshot recovery, events are buffered or withheld by the bridge until the snapshot baseline is established; the client never guesses the cutoff.
10. A revision gap or queue-loss recovery requires a new `snapshot_request` before the state is presented as current; duplicate or stale events at or below the current revision are ignored.

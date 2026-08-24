# Protocol schema

## Common envelope

Every message is one UTF-8 JSON object with these fields. The transport preserves one complete
object per message; framing is not part of the JSON payload.

```json
{
  "messageType": "state_snapshot",
  "messageId": "opaque-message-id",
  "sessionId": "opaque-connection-id",
  "correlationId": null,
  "payload": {},
  "bridgeInstanceId": "opaque-bridge-instance-id",
  "playContextId": null,
  "clientId": null
}
```

| Field | Type | Required | Meaning |
|---|---|---:|---|
| `messageType` | string enum | yes | Canonical message identifier; exactly one registered message type. |
| `messageId` | string | yes | Cryptographically random and unique within the connection session; duplicate IDs are rejected. |
| `sessionId` | string or `null` | yes | `null` for pre-authentication `hello`, and `null` for an `error` that rejects a connection before any session was established on that socket (for example an auth failure or a violation detected before decoding completes). `hello_ack` and every other message carry the server-issued identity for that socket; an `error` reported after a session exists carries that session's identity. A session ID is valid only on the socket to which it was issued. |
| `correlationId` | string or `null` | yes | Message ID being answered, or `null` when there is no correlation; response rules are defined below. |
| `payload` | object | yes | Message-specific data. |
| `bridgeInstanceId` | string or `null` | yes | Identifies the running bridge process; changes on every bridge restart. `null` on the client's own `hello` (the client does not know it yet) and when this Bridge process could not generate its own identity at startup; present on every Bridge-originated message otherwise, including error responses. |
| `playContextId` | string or `null` | yes | Identifies the currently loaded play context. `null` outside an active play context (main menu, before any load, or after a return to the main menu) — genuine semantic absence, not a placeholder. |
| `clientId` | string or `null` | yes | Identifies the logical client, established at `hello`. `null` on the client's own `hello` (not yet established) and on every message the Bridge sends after `hello_ack`: once a session exists, the Bridge derives the authenticated client from that session rather than repeating it on the wire. `hello_ack` itself still carries the value it accepted, confirming the identity the session now owns. |

Unknown top-level fields are ignored only when forward-compatible reading is permitted by the
current schema. Required fields with the wrong type invalidate the message. Every message type below
lists its required payload fields; fields not listed are not sent.

Compatibility with this schema is identified by the DovahLink Bridge/mod release version, not an
independent protocol-generation number carried on every message — see
[`ai/context/protocol/compatibility.md`](../../ai/context/protocol/compatibility.md).

## State envelope

`state_snapshot` and `state_event` payloads contain:

```json
{
  "stateArea": "example_area",
  "revision": 42,
  "occurredAt": "2026-08-10T12:00:00Z",
  "data": {}
}
```

- `stateArea` is a canonical identifier assigned when a state area is registered. No state area is
  currently registered; see "Registered state areas" below.
- `revision` is a non-negative integer, monotonically increasing within one
  `(bridgeInstanceId, playContextId, stateArea)`.
- A revision belongs to that authoritative bridge instance, play context, and state area rather
  than to a socket session: it advances only when that authoritative state changes, is not reset by
  a reconnect, and is invalidated when the play context changes. Clients detect stale cached state
  by comparing `(bridgeInstanceId, playContextId)` against what they have cached; a mismatch means
  the cached state came from a different bridge lifetime or play context and must be discarded
  before the next snapshot is trusted.
- `occurredAt` is UTC RFC 3339 wall-clock time for display and diagnostics; it is not an ordering source.
- `data` contains the state-area contract.
- An unavailable value is represented explicitly as `null` or by the state-area's documented availability field; it must not be replaced with a plausible default.

An event additionally contains `baseRevision`, the revision the event expects the client to have before applying it:

```json
{
  "stateArea": "example_area",
  "baseRevision": 41,
  "revision": 42,
  "occurredAt": "2026-08-10T12:00:00Z",
  "data": {}
}
```

`data` is the complete post-change state for the state area, not a patch. `revision` must equal `baseRevision + 1`. If an event's `revision` is at or below the client's current revision, the client ignores it as a duplicate or stale message. If its `revision` is higher than the current revision and `baseRevision` does not equal the current revision, the client marks the state area stale and requests a fresh snapshot.

An accepted snapshot becomes the baseline for its state area and supersedes older events for that
area.

## Registered state areas

No state area is currently registered. The previous `character` aggregate (player level and three
resource pools bundled into one state area) is retired: a future phase may register a composed
character view, or focused progression-specific areas, without reviving this shape. Until a state
area is registered, `subscribe` and `snapshot_request` reject every requested area explicitly (see
their sections below) and both endpoints' `capabilities` list is empty.

## Message types

`messageType` is a closed canonical vocabulary. Its complete set is `hello`, `hello_ack`,
`pairing_request`, `pairing_status`, `pairing_confirm`, `pairing_ack`, `pairing_renotify`,
`pairing_cancel`, `pairing_outcome`, `rename_request`, `rename_outcome`, `capabilities`,
`subscribe`, `subscription_ack`, `snapshot_request`, `state_snapshot`, `state_event`, `error`,
`session_invalidated`, `ping`, and `pong`. A Bridge/SDK compatibility check occurs immediately
after `hello_ack`; an unrecognized message type is malformed protocol input and is rejected rather
than interpreted as a forward-compatible value.

### `hello`

Negotiates the connection before any optional state messages. The connecting client always sends
`hello` first; the bridge never initiates a connection or sends `hello` itself, and only replies
with `hello_ack` after it receives and validates one.

```json
{
  "endpoint": "client",
  "clientId": "opaque-client-id",
  "auth": {
    "method": "trusted_device_credential",
    "token": "redacted-in-documentation"
  }
}
```

`endpoint` identifies the sender's role and is always `client`, because only the connecting client
sends `hello`. `clientId` identifies the logical client/installation independently of any
connection; it persists across reconnects and is not itself a trust credential.

`auth.method` is one of:

- `one_time_local_token` — developer/loopback-proof authentication against the process-lifetime
  one-time token. `auth.token` is required.
- `unpaired` — no credential presented yet. Admits a session restricted to
  `ping`/`capabilities`/`pairing_request`/`pairing_confirm`/`pairing_ack` until pairing succeeds
  (see the pairing messages below). `auth.token` must be absent.
- `trusted_device_credential` — a persisted pairing credential, for an ordinary reconnect.
  `auth.token` carries the hex-encoded credential and is required.

See [`ai/context/protocol/security.md`](../../ai/context/protocol/security.md)'s "Hello
authentication and session trust tiers" for the full design.

Required payload fields: `endpoint`, `clientId`, `auth`. `auth.token` is required for
`one_time_local_token` and `trusted_device_credential`, and must be absent for `unpaired`. The peer
responds with `hello_ack` only after authentication succeeds.

### `hello_ack`

Carries the newly issued non-null `sessionId` in its envelope, and exposes the bootstrap
compatibility information a client needs before trusting the rest of the exchange:

```json
{
  "bridgeVersion": "0.3.2",
  "clientIdentityKind": "paired"
}
```

`bridgeVersion` is a required, non-empty string containing the DovahLink Bridge/mod release version
(matching `bridge/vcpkg.json`'s `version-string`). The bridge always answers a validated `hello` with `hello_ack`; it does not
receive or evaluate a client-declared compatibility range itself. Checking `bridgeVersion` against
its own declared supported range, and failing explicitly on a mismatch, is the client/SDK's
responsibility — see `ai/context/protocol/compatibility.md`'s compatibility bootstrap.

`clientIdentityKind` is `"unpaired"` for a session admitted via `auth.method: one_time_local_token`
or `unpaired` (trust-restricted until pairing succeeds), or `"paired"` for a session admitted via
`trusted_device_credential`, or a restricted session upgraded in place by a successful
`pairing_ack` — the upgrade happens on the same connection with no reconnect and no `sessionId`
change, so a client only learns of it from that `pairing_outcome`, not from a fresh `hello_ack`.

Required payload fields: `bridgeVersion`, `clientIdentityKind`.

`hello_ack.correlationId` is the `messageId` of the `hello` it answers.

### `pairing_request`

Client request to start, or query the status of, a pairing challenge. Sent on a Restricted session
only — an already-trusted (Full) session has no reason to re-pair, and the bridge rejects pairing
messages on one.

```json
{}
```

No payload fields, matching `ping`'s empty-payload precedent. The bridge responds with
`pairing_status`.

### `pairing_status`

Bridge report of pairing availability, sent in reply to `pairing_request`:

```json
{
  "state": "available",
  "expiresInSeconds": 287
}
```

`state` is one of `"unavailable"`, `"available"`, `"in_progress"`, or `"other_device_pairing"`.
`"available"` means a fresh six-digit code was just generated and displayed to the user in Skyrim; a
repeated `pairing_request` from the same `clientId` reports `"in_progress"` instead, without
generating or displaying a second code -- covering *both* of `clientId`'s own resumable states:
its code is still active and counting down (`CHALLENGE_ACTIVE`), or it already submitted the
correct code and is now holding a pending credential awaiting `pairing_ack` (`PENDING_CREDENTIAL`,
`ai/context/protocol/security.md`'s "Persistent local trust" state machine). `"other_device_pairing"`
means a *different* `clientId` currently owns the active challenge or pending credential; it
discloses nothing else about the owning device or its code, and `expiresInSeconds` is never present
alongside it.

`expiresInSeconds` is the active challenge's remaining *code* validity -- not a general "how long
until you lose this state" figure -- so its presence follows the code's own lifetime, not `state`
alone:
- A number, for `"available"` and for `"in_progress"` while `clientId`'s own code is still
  `CHALLENGE_ACTIVE` and actively counting down.
- `null` (the key present, valued `null`) for `"unavailable"`, and for `"in_progress"` while
  `clientId`'s own resumed state is `PENDING_CREDENTIAL` -- the code was already consumed on a
  successful `pairing_confirm`, so there is no code left to count down, even though the client still
  has something of its own to resume (per `PairingSession::RemainingSeconds`'s own contract: no
  value once no challenge is active, "including a `PENDING_CREDENTIAL` state, which has no code left
  to redisplay"). This is a real, reachable case, not a defect: a client that reconnects, crashes, or
  otherwise probes `pairing_request` again after confirming a code but before its `pairing_ack` has
  landed observes exactly this.
- Omitted from the payload entirely (not merely `null`) for `"other_device_pairing"` -- see above.

Required payload field: `state`. `expiresInSeconds` is always present as `null` unless one of the
notes above says it carries a number or is omitted entirely. Absent and `null` are not
interchangeable on this field: omission is reserved for `"other_device_pairing"` specifically, and
every other state that has no number to report still carries the key with a `null` value.

`pairing_status.correlationId` is the `messageId` of the `pairing_request` it answers.

### `pairing_confirm`

Client submission of the six-digit code the user read from Skyrim and entered:

```json
{
  "code": "redacted-in-documentation",
  "displayName": "My PC"
}
```

`code` is required. `displayName` is an optional, presentation-only label for the resulting trusted
client; send `null` when omitted, which preserves the client's existing display name on a re-pair
(a genuinely new client stays unnamed). A present value -- including an empty string, which clears
the name -- always replaces whatever the client previously held. The bridge responds with
`pairing_outcome` (`"credential_issued"`, `"expired"`, `"invalid"`, `"pacing_limited"`, or
`"hard_limit_reached"`).

Required payload field: `code`.

### `pairing_ack`

Client's final confirmation, echoing back the credential it durably saved:

```json
{
  "credential": "redacted-in-documentation"
}
```

`credential` is the hex-encoded credential the client received in a prior `credential_issued`
outcome, saved to persistent storage before this message is sent. The bridge responds with
`pairing_outcome` (`"trusted"`, `"already_trusted"`, or `"pending_not_found"`), and on `"trusted"`
upgrades the session to full trust in place on the same connection — no reconnect required.

Required payload field: `credential`.

### `pairing_renotify`

Client request to redisplay the active pairing challenge's code. Sent on a Restricted session only.
Never generates a new code and never sends the code itself over the wire — redisplay occurs through
the in-game notification, not the connection.

```json
{}
```

No payload fields. Only the owning `clientId` may invoke it. The bridge responds with `pairing_outcome`
(`"renotified"` on success, `"renotify_cooldown"` with remaining wait, or `"already_idle"` when no
challenge is owned).

### `pairing_cancel`

Client request to give up an owned active challenge or pending credential. Sent on a Restricted
session only. Never touches persisted trust or any already-committed credentials — only clears
in-memory challenge/pending state and frees the slot for a fresh `pairing_request`.

```json
{}
```

No payload fields. Only the owning `clientId` may invoke it. The bridge responds with `pairing_outcome`
(`"cancelled"` if something was cleared, or `"already_idle"` if nothing was owned). Idempotent without
pretending work occurred — repeating it truthfully reports `"already_idle"`, not `"cancelled"`.

### `pairing_outcome`

Shared bridge reply to both `pairing_confirm` and `pairing_ack`, distinguished by `outcome`:

```json
{
  "outcome": "trusted",
  "credential": "redacted-in-documentation",
  "shortId": "12345",
  "displayName": "My PC",
  "retryAfterSeconds": null
}
```

`outcome` is one of `"credential_issued"`, `"trusted"`, `"already_trusted"`, `"expired"`,
`"invalid"`, `"pacing_limited"`, `"hard_limit_reached"`, `"pending_not_found"`, `"renotified"`,
`"renotify_cooldown"`, `"cancelled"`, or `"already_idle"`. Outcomes are grouped by originating message:
- From `pairing_confirm`: `"credential_issued"`, `"expired"`, `"invalid"`, `"pacing_limited"`,
  `"hard_limit_reached"`. `"pacing_limited"` and `"hard_limit_reached"` replace the single
  undifferentiated `"rate_limited"` earlier phases used: pacing rejects an attempt made too soon
  after the previous one, without counting it as wrong, while the hard limit is the terminal count
  of wrong attempts that cancels the challenge outright.
- From `pairing_ack`: `"trusted"`, `"already_trusted"`, `"pending_not_found"`.
- From `pairing_renotify`: `"renotified"`, `"renotify_cooldown"`, `"already_idle"`.
- From `pairing_cancel`: `"cancelled"`, `"already_idle"`.

`credential` is present only for `"credential_issued"`, `"trusted"`, and `"already_trusted"`.
`shortId` (an administration-only identifier, not a trust credential) is present only for `"trusted"`
and `"already_trusted"`. `displayName` echoes the client-supplied label and is present only alongside
`credential`/`shortId` when the client supplied one. `retryAfterSeconds` is the minimum safe number of
whole seconds to wait before retrying, rounded upward whenever a positive fractional wait remains.
It is present for `"pacing_limited"` (next evaluated `pairing_confirm` attempt) and
`"renotify_cooldown"` (next manual `pairing_renotify`).

Required payload field: `outcome`. `credential`, `shortId`, `displayName`, and `retryAfterSeconds`
are always present in the payload as `null` unless the note above says otherwise.

`pairing_outcome.correlationId` is the `messageId` of the `pairing_confirm` or `pairing_ack` it
answers.

### `rename_request`

Client request to rename itself. Sent on a Full session only -- an already-trusted device renames
itself directly; an unpaired/restricted session has nothing to rename.

```json
{
  "displayName": "New Name"
}
```

`displayName` is required and may be empty; an empty value clears the device's display name,
matching `ai/context/protocol/security.md`'s "displayName stays presentation-only metadata" rule. A
non-empty value is subject to the trust store's length and control-character bound. The bridge
responds with `rename_outcome`.

Required payload field: `displayName`.

### `rename_outcome`

Bridge reply to `rename_request`:

```json
{
  "outcome": "renamed",
  "displayName": "New Name"
}
```

`outcome` is one of `"renamed"`, `"invalid_display_name"`, or `"not_trusted"`. `"not_trusted"`
covers both an unrecognized identity and one that is known but not currently trusted -- there is
nothing actionable a connected client can do differently between those two cases, and this phase's
trust-tier design (`ai/context/protocol/security.md`'s "Hello authentication and session trust
tiers") makes it unreachable in practice: a Full session's owner is always currently trusted, since
Block and Revoke immediately tear down the session that owns the credential being invalidated.
`displayName` echoes the resulting name and is present only for `"renamed"`; `null` when the rename
cleared the name or for any other outcome.

Required payload field: `outcome`. `displayName` is always present in the payload as `null` unless
the note above says otherwise.

`rename_outcome.correlationId` is the `messageId` of the `rename_request` it answers.

### `capabilities`

Declares supported features after `hello_ack`.

```json
{
  "capabilities": []
}
```

Capability IDs and versions are canonical protocol values, independent of the Bridge release
version. A missing capability means the feature is unavailable and the client must remain usable
without it.

Required payload field: `capabilities`. Each capability requires `id` and `version`.

Both endpoints send `capabilities`. No capability is currently registered (see "Registered state
areas" above); both the bridge and the client send an empty list, and any non-empty list is
rejected as `unsupported_capability`.

### `subscribe`

Requests state areas after capabilities are negotiated.

```json
{
  "stateAreas": ["example_area"]
}
```

The bridge confirms the subscription and sends a `state_snapshot` before sending events for that state area.

Required payload field: `stateAreas`. The bridge responds with `subscription_ack`. No state area is
currently registered (see "Registered state areas" above), so every requested area is rejected into
`subscription_ack.rejectedStateAreas`.

### `subscription_ack`

Confirms accepted and rejected state areas:

```json
{
  "acceptedStateAreas": [],
  "rejectedStateAreas": ["example_area"]
}
```

Both arrays are required. The bridge sends snapshots only for accepted areas. No state area is
currently registered, so `acceptedStateAreas` is always empty and every requested area appears in
`rejectedStateAreas`.

`subscription_ack.correlationId` is the `messageId` of the `subscribe` it answers. Each initial snapshot also uses the `subscribe` message ID as its `correlationId`; later snapshots use the `snapshot_request` message ID that caused them.

### `snapshot_request`

Requests a fresh baseline for one state area. The bridge responds with a `state_snapshot` at the current revision.

```json
{
  "stateArea": "example_area",
  "knownRevision": 41
}
```

Required payload field: `stateArea`. `knownRevision` is optional and advisory only. No state area
is currently registered (see "Registered state areas" above), so every request is rejected as
`unsupported_capability`.

### `state_snapshot`

Contains the complete state for one subscribed state area at a revision.

Required payload fields: `stateArea`, `revision`, `occurredAt`, `data`.

An initial snapshot correlates to `subscribe`; a recovery snapshot correlates to `snapshot_request`.

### `state_event`

Contains one ordered update from `baseRevision` to `revision` for one subscribed state area.

Required payload fields: `stateArea`, `baseRevision`, `revision`, `occurredAt`, `data`. Events contain complete post-change state, not partial patches.

### `error`

Reports a structured failure without exposing infrastructure exceptions:

```json
{
  "code": "unauthenticated",
  "message": "Token validation failed",
  "retryable": false,
  "details": null
}
```

`code` is a canonical machine-readable value. `message` is diagnostic text and must not be used for branching.

Required payload fields: `code`, `message`, `retryable`. `details` is nullable and optional when no safe diagnostic details exist.

Canonical error codes are exactly `malformed_message`, `frame_too_large`, `unsupported_capability`,
`unauthenticated`, `unauthorized`, `revoked`, `blocked`, `replayed_message`, `stale_session`,
`rate_limited`, and `internal_error`. `revoked` is a `trusted_device_credential` hello rejected
because the presented `clientId` was explicitly revoked, distinct from `unauthenticated`'s "never
paired or wrong credential" per `ai/context/protocol/security.md`'s "Persistent local trust".
`blocked` is an `unpaired` or `trusted_device_credential` hello rejected because the presented
`clientId` is a currently blocked Known Device -- distinct from `revoked` (blocking prevents both
authentication and re-pairing, while a revoked device may still re-pair) and never issued for
`one_time_local_token` (developer-token) authentication, which stays a separate provider unaffected
by Known Device blocking. Error codes are for branching; diagnostic messages are not. There is no
Bridge-version-incompatibility wire error code: a client detects incompatibility itself from
`hello_ack.bridgeVersion` and fails without completing the rest of the exchange, per
`ai/context/protocol/compatibility.md`.

If no session has been established on a socket, an `error`'s `sessionId` is `null`; this includes
authentication failures and violations detected before decoding completes. After a successful
`hello_ack`, errors carry the active session identity.

### `session_invalidated`

An unsolicited, Bridge-originated terminal event for an authenticated session that an administrator
deliberately invalidated. It is sent best-effort before the Bridge force-closes the affected socket;
the event is not a security boundary, requires no acknowledgement, and may be absent when delivery
is impossible.

```json
{
  "reason": "revoked"
}
```

Required payload field: `reason`. It is one of `"revoked"`, `"blocked"`, `"trust_reset"`, or
`"factory_reset"`. The event's `correlationId` is `null`. `"trust_reset"` means a Trusted Known
Device became Revoked; `"factory_reset"` may terminate developer-token sessions even though the
configured developer token remains valid. Clients must treat the event as terminal for the current
session and must not wait for an acknowledgement before local cleanup or disconnect handling.

### `ping` and `pong`

Carry no application state. They prove liveness for the current `sessionId`.

`pong.correlationId` is the `messageId` of the `ping` it answers. `capabilities`, `state_event`, and unsolicited `error` messages use `correlationId: null`.

## Session and recovery rules

1. The connecting client sends `hello`.
2. The bridge authenticates the client and replies with `hello_ack`, which exposes the bridge's
   release version for the client to evaluate against its own declared supported range. The bridge
   does not reject a connection on compatibility grounds; it does not receive or evaluate a
   client-declared version range itself. A client that finds the exposed version outside its
   supported range fails explicitly on its own side rather than continuing the exchange.
3. They exchange `capabilities`.
4. The client sends `subscribe` and receives `subscription_ack`.
5. The bridge sends a snapshot before events for each accepted state area.
6. Each authenticated socket receives a unique `sessionId`. The session is bound exclusively to
   that socket and is invalidated when the socket closes for any reason; an administrative
   `session_invalidated` event may be sent before the force-close, but delivery is best-effort.
7. A session cannot be transferred, resumed, or reused on another socket. A reconnect creates a
   new session, and messages carrying an invalidated or foreign session ID are rejected as
   `stale_session` before application handling.
8. The client must not apply messages from its previous session. Queued state from that session is
   not replayed; a fresh snapshot establishes each new
   baseline.
9. During snapshot recovery, events are buffered or withheld by the bridge until the snapshot baseline is established; the client never guesses the cutoff.
10. A revision gap or queue-loss recovery requires a new `snapshot_request` before the state is presented as current; duplicate or stale events at or below the current revision are ignored.

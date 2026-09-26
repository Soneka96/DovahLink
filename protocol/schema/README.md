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
  "stateAuthorityId": "opaque-state-authority-id",
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
| `stateAuthorityId` | string | conditional | Identifies the Host's current authoritative-state continuity epoch: it changes only when an event breaks revision continuity (a Host restart, or an Adapter/IPC connection loss detected — never a mere reconnect, new session, or play-context change). Required and non-null on `hello_ack`, `state_snapshot`, and `state_event`; absent on every other message, including every client-originated one. See `ai/context/protocol/compatibility.md` for the full continuity-epoch definition. |
| `playContextId` | string or `null` | yes | Identifies the currently loaded play context. `null` outside an active play context (main menu, before any load, or after a return to the main menu) — genuine semantic absence, not a placeholder. |
| `clientId` | string or `null` | yes | Identifies the logical client, established at `hello`. `null` on the client's own `hello` (not yet established) and on every message the Host sends after `hello_ack`: once a session exists, the Host derives the authenticated client from that session rather than repeating it on the wire. `hello_ack` itself still carries the value it accepted, confirming the identity the session now owns. |

Unknown top-level fields are ignored only when forward-compatible reading is permitted by the
current schema. Required fields with the wrong type invalidate the message. Every message type below
lists its required payload fields; fields not listed are not sent.

Compatibility with this schema is identified by the Host's own release version, carried in
the `hostVersion` wire field, not an
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

- `stateArea` is a canonical identifier assigned when a state area is registered; see "Registered
  state areas" below for the five areas currently registered.
- `revision` is a non-negative integer, monotonically increasing within one
  `(stateAuthorityId, playContextId, stateArea)`.
- A revision belongs to that authority continuity epoch, play context, and state area rather
  than to a socket session: it advances only when that authoritative state changes, is not reset by
  a reconnect, and is invalidated when the play context changes. Clients detect stale cached state
  by comparing `(stateAuthorityId, playContextId)` against what they have cached; a mismatch means
  the cached state came from a different authority continuity epoch or play context and must be
  discarded before the next snapshot is trusted.
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

The Host currently registers five Character state areas, each independently authoritative:

| State area | Value meaning | Public `data` value type | Delivery mode | Capture policy | Unavailable behavior |
|---|---|---|---|---|---|
| `character_xp` | Current character experience/progression value | JSON number (single-precision reading) | Snapshot | Sampled on its own, at Medium cadence | `"value": null` |
| `character_health` | Current health | JSON number (single-precision reading) | Snapshot | Sampled at Fast cadence, as part of one coherent Vitals capture together with magicka and stamina | `"value": null` |
| `character_magicka` | Current magicka | JSON number (single-precision reading) | Snapshot | Sampled at Fast cadence, same coherent Vitals capture | `"value": null` |
| `character_stamina` | Current stamina | JSON number (single-precision reading) | Snapshot | Sampled at Fast cadence, same coherent Vitals capture | `"value": null` |
| `character_level` | Current level | JSON number (integer-valued, 0-65535) | Event | Its initial/recovery baseline is established by a dedicated resynchronization-only sample, delivered as a `state_snapshot`; native level-up occurrences then publish as `state_event` | `"value": null` |

Every one of the five areas uses the same public `data` shape, with `value` as its only field:

```json
{
  "value": 42.5
}
```

`value` is `null` when the state is legitimately unavailable -- the fail-closed default; capture
never substitutes a plausible default such as `0`, full health, or level `1`. A malformed or
unrecognized private capture is a distinct case from a legitimate unavailable reading: it never
reaches the public contract as a null value either, and instead produces no publication at all for
that update.

`character_xp`, `character_health`, `character_magicka`, and `character_stamina` are Snapshot-only:
the Host has no Event-domain update for them, and only ever revises their value at a new `revision`
via `state_snapshot`, through the normal subscribe/snapshot_request/recovery rules above.

`character_level`'s canonical delivery mode is Event, but native level changes are not its only
source of state: its initial or recovery value is established the same way as the four Snapshot-only
areas above, as a `state_snapshot`, before any Event is delivered. This baseline delivery does not
change the area's canonical Event mode -- it is how an Event-mode area still gives a client a
starting value to apply Events against. Once established, subsequent native level changes are
delivered as `state_event`: `revision` equals `baseRevision + 1`, and `data` carries the complete
post-change value, not a delta, per the general event rule above. A client must not treat
`character_level` as usable before it has received a baseline Snapshot; the Event stream alone is
not a valid starting point.

The retired `character` aggregate (player level and three resource pools bundled into one state
area) is not revived by this. `character_xp`, `character_health`, `character_magicka`,
`character_stamina`, and `character_level` are five independent, separately-subscribable state
areas, not facets of one composed view.

Host/Adapter resynchronization establishes a fresh authoritative baseline for these areas after
continuity recovery or an active play-context transition; this is why a `state_snapshot` for an
already-subscribed area can arrive without a client-initiated `snapshot_request`. The client always
receives an authoritative Snapshot from the Host -- it never reads Skyrim state directly.

An area requested by `subscribe` or `snapshot_request` that is not one of these five remains
explicitly rejected (see their sections below).

### Registered state area examples

`character_xp` snapshot:

```json
{
  "messageType": "state_snapshot",
  "messageId": "b3f1a2c4-6d5e-4a1b-9c3d-7e8f9a0b1c2d",
  "sessionId": "0f1e2d3c-4b5a-4968-8778-90a1b2c3d4e5",
  "correlationId": "a1b2c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d",
  "payload": {
    "stateArea": "character_xp",
    "revision": 7,
    "occurredAt": "2026-08-10T12:00:03Z",
    "data": { "value": 1280.0 }
  },
  "stateAuthorityId": "9f2c1a3e-5b6d-4c7a-8e9f-0a1b2c3d4e5f",
  "playContextId": "4b7ad2f1-6c8e-4a9b-9d0e-1f2a3b4c5d6e",
  "clientId": null
}
```

`character_health` snapshot, one member of the same coherent Vitals capture as `character_magicka`
and `character_stamina`:

```json
{
  "messageType": "state_snapshot",
  "messageId": "c4d5e6f7-8a9b-4c0d-9e1f-2a3b4c5d6e7f",
  "sessionId": "0f1e2d3c-4b5a-4968-8778-90a1b2c3d4e5",
  "correlationId": "a1b2c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d",
  "payload": {
    "stateArea": "character_health",
    "revision": 12,
    "occurredAt": "2026-08-10T12:00:03Z",
    "data": { "value": 87.5 }
  },
  "stateAuthorityId": "9f2c1a3e-5b6d-4c7a-8e9f-0a1b2c3d4e5f",
  "playContextId": "4b7ad2f1-6c8e-4a9b-9d0e-1f2a3b4c5d6e",
  "clientId": null
}
```

`character_level` initial/recovery baseline snapshot, correlated to the `subscribe` that requested
it:

```json
{
  "messageType": "state_snapshot",
  "messageId": "d5e6f7a8-9b0c-4d1e-8f2a-3b4c5d6e7f80",
  "sessionId": "0f1e2d3c-4b5a-4968-8778-90a1b2c3d4e5",
  "correlationId": "a1b2c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d",
  "payload": {
    "stateArea": "character_level",
    "revision": 4,
    "occurredAt": "2026-08-10T12:00:03Z",
    "data": { "value": 10 }
  },
  "stateAuthorityId": "9f2c1a3e-5b6d-4c7a-8e9f-0a1b2c3d4e5f",
  "playContextId": "4b7ad2f1-6c8e-4a9b-9d0e-1f2a3b4c5d6e",
  "clientId": null
}
```

The subsequent native level-up, delivered as an unsolicited Event with `revision == baseRevision +
1` and the complete post-change value:

```json
{
  "messageType": "state_event",
  "messageId": "e6f7a8b9-0c1d-4e2f-8a3b-4c5d6e7f8091",
  "sessionId": "0f1e2d3c-4b5a-4968-8778-90a1b2c3d4e5",
  "correlationId": null,
  "payload": {
    "stateArea": "character_level",
    "baseRevision": 4,
    "revision": 5,
    "occurredAt": "2026-08-10T12:05:41Z",
    "data": { "value": 11 }
  },
  "stateAuthorityId": "9f2c1a3e-5b6d-4c7a-8e9f-0a1b2c3d4e5f",
  "playContextId": "4b7ad2f1-6c8e-4a9b-9d0e-1f2a3b4c5d6e",
  "clientId": null
}
```

## Message types

`messageType` is a closed canonical vocabulary. Its complete set is `hello`, `hello_ack`,
`pairing_request`, `pairing_status`, `pairing_confirm`, `pairing_ack`, `pairing_renotify`,
`pairing_cancel`, `pairing_outcome`, `rename_request`, `rename_outcome`, `capabilities`,
`subscribe`, `subscription_ack`, `snapshot_request`, `state_snapshot`, `state_event`, `error`,
`session_invalidated`, `ping`, and `pong`. A Host/SDK compatibility check occurs immediately
after `hello_ack`; an unrecognized message type is malformed protocol input and is rejected rather
than interpreted as a forward-compatible value.

### `hello`

Negotiates the connection before any optional state messages. The connecting client always sends
`hello` first; the host never initiates a connection or sends `hello` itself, and only replies
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
  `ping`/`capabilities`/`pairing_request`/`pairing_confirm`/`pairing_ack`/`pairing_renotify`/
  `pairing_cancel` until pairing succeeds (see the pairing messages below). `auth.token` must be
  absent.
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
  "hostVersion": "0.5.0",
  "hostId": "81869993-955c-4ba3-a7d0-d35ca86078ea",
  "hostName": "GONCALO-DESKTOP",
  "clientIdentityKind": "paired"
}
```

`hostId` is the stable, DovahLink-generated UUID of this Host installation. It is exactly one
non-empty UUID string and is independent of all machine, process, client, session, and transport
values. `hostName` is the current operating-system computer name, or the Host's safe fallback; it is
required, non-empty, free of control characters, and at most 64 UTF-8 bytes. The name is display
metadata and may change without changing `hostId`. `endpoint` is a mutable connection location and
is not part of this payload or Host identity.

`hostVersion` is a required, non-empty string containing the Host's own release version, the
compatibility authority per `ai/context/protocol/compatibility.md`. The host
always answers a validated `hello` with `hello_ack`; it does not
receive or evaluate a client-declared compatibility range itself. Checking `hostVersion` against
its own declared supported range, and failing explicitly on a mismatch, is the client/SDK's
responsibility — see `ai/context/protocol/compatibility.md`'s compatibility bootstrap.
`clientIdentityKind` is `"unpaired"` for a session admitted via `auth.method: one_time_local_token`
or `unpaired` (trust-restricted until pairing succeeds), or `"paired"` for a session admitted via
`trusted_device_credential`, or a restricted session upgraded in place by a successful
`pairing_ack` — the upgrade happens on the same connection with no reconnect and no `sessionId`
change, so a client only learns of it from that `pairing_outcome`, not from a fresh `hello_ack`.

Required payload fields: `hostVersion`, `hostId`, `hostName`, `clientIdentityKind`.

`hello_ack.correlationId` is the `messageId` of the `hello` it answers.

### `pairing_request`

Client request to start, or query the status of, a pairing challenge. Sent on a Restricted session
only — an already-trusted (Full) session has no reason to re-pair, and the host rejects pairing
messages on one.

```json
{}
```

No payload fields, matching `ping`'s empty-payload precedent. The host responds with
`pairing_status`.

### `pairing_status`

Host report of pairing availability, sent in reply to `pairing_request`:

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
the name -- always replaces whatever the client previously held. The host responds with
`pairing_outcome` (`"credential_issued"`, `"expired"`, `"invalid"`, `"pacing_limited"`,
`"hard_limit_reached"`, or `"pairing_invalidated"`). `pairing_invalidated` here means the presented
code was genuinely correct, but an administrative mutation (Revoke, Block, Reset Trust, or Factory
Reset) committed after this challenge began, so it never issued a credential -- the client discards
it and restarts pairing, the same reaction it already has for an ACK-time `pairing_invalidated`.

Required payload field: `code`.

### `pairing_ack`

Client's final confirmation, echoing back the credential it durably saved:

```json
{
  "credential": "redacted-in-documentation"
}
```

`credential` is the hex-encoded credential the client received in a prior `credential_issued`
outcome, saved to persistent storage before this message is sent. The host responds with
`pairing_outcome` (`"trusted"`, `"already_trusted"`, `"pending_not_found"`, or
`"pairing_invalidated"`), and on `"trusted"` upgrades the session to full trust in place on the
same connection — no reconnect required.

Required payload field: `credential`.

### `pairing_renotify`

Client request to redisplay the active pairing challenge's code. Sent on a Restricted session only.
Never generates a new code and never sends the code itself over the wire — redisplay occurs through
the in-game notification, not the connection.

```json
{}
```

No payload fields. Only the owning `clientId` may invoke it. The host responds with `pairing_outcome`
(`"renotified"` on success, `"renotify_cooldown"` with remaining wait, or `"already_idle"` when no
challenge is owned).

### `pairing_cancel`

Client request to give up an owned active challenge or pending credential. Sent on a Restricted
session only. Never touches persisted trust or any already-committed credentials — only clears
in-memory challenge/pending state and frees the slot for a fresh `pairing_request`.

```json
{}
```

No payload fields. Only the owning `clientId` may invoke it. The host responds with `pairing_outcome`
(`"cancelled"` if something was cleared, or `"already_idle"` if nothing was owned). Idempotent without
pretending work occurred — repeating it truthfully reports `"already_idle"`, not `"cancelled"`.

### `pairing_outcome`

Shared host reply to both `pairing_confirm` and `pairing_ack`, distinguished by `outcome`:

```json
{
  "outcome": "trusted",
  "credential": "redacted-in-documentation",
  "shortId": "12345",
  "displayName": "My PC",
  "attemptsRemaining": null,
  "retryAfterSeconds": null
}
```

`outcome` is one of `"credential_issued"`, `"trusted"`, `"already_trusted"`, `"expired"`,
`"invalid"`, `"pacing_limited"`, `"hard_limit_reached"`, `"pending_not_found"`,
`"pairing_invalidated"`, `"renotified"`,
`"renotify_cooldown"`, `"cancelled"`, or `"already_idle"`. Outcomes are grouped by originating message:
- From `pairing_confirm`: `"credential_issued"`, `"expired"`, `"invalid"`, `"pacing_limited"`,
  `"hard_limit_reached"`, `"pairing_invalidated"`. `"pacing_limited"` and `"hard_limit_reached"`
  replace the single undifferentiated `"rate_limited"` earlier phases used: pacing rejects an
  attempt made too soon after the previous one, without counting it as wrong, while the hard limit
  is the terminal count of wrong attempts that cancels the challenge outright. `pairing_invalidated`
  here means the presented code matched, but an administrative mutation committed after this
  challenge began, before a credential was ever issued for it.
- From `pairing_ack`: `"trusted"`, `"already_trusted"`, `"pending_not_found"`,
  `"pairing_invalidated"`. `pending_not_found` means no matching in-memory pending
  credential remained, such as after Host restart, expiry, or a mismatched credential.
  `pairing_invalidated` here means the matching pending credential was consumed but an
  administrative mutation invalidated its trust fence; the client must discard it and
  restart pairing -- the same reaction as the `pairing_confirm`-time occurrence above.
- From `pairing_renotify`: `"renotified"`, `"renotify_cooldown"`, `"already_idle"`.
- From `pairing_cancel`: `"cancelled"`, `"already_idle"`.

`credential` is present only for `"credential_issued"`, `"trusted"`, and `"already_trusted"`.
`shortId` (an administration-only identifier, not a trust credential) is present only for `"trusted"`
and `"already_trusted"`. `displayName` echoes the client-supplied label and is present only alongside
`credential`/`shortId` when the client supplied one. `retryAfterSeconds` is the Host-authoritative
number of whole seconds until the relevant operation may safely be retried, rounded upward whenever
a positive fractional wait remains. It is present for `"pacing_limited"` (next evaluated
`pairing_confirm` attempt), `"renotify_cooldown"` (next manual `pairing_renotify`), and
`"renotified"` (the cooldown that just began after the Adapter accepted the redisplay and the Host
committed it).

`attemptsRemaining` is the Host-authoritative number of wrong-code attempts remaining after an
`"invalid"` result that counted a wrong code against the caller's active challenge. It is `null`
when `"invalid"` did not count an attempt (for example, no owned challenge or an uncommitted initial
display), and for every other outcome, including `"hard_limit_reached"`. Clients must not derive
this value from a locally configured maximum.

Required payload field: `outcome`. `credential`, `shortId`, `displayName`, `attemptsRemaining`, and
`retryAfterSeconds` are always present in the payload as `null` unless the note above says otherwise.

`pairing_outcome.correlationId` is the `messageId` of the `pairing_confirm`, `pairing_ack`,
`pairing_renotify`, or `pairing_cancel` it answers.

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
non-empty value is subject to the trust store's length and control-character bound. The host
responds with `rename_outcome`.

Required payload field: `displayName`.

### `rename_outcome`

Host reply to `rename_request`:

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

Capability IDs and versions are canonical protocol values, independent of the DovahLink product
release version. A missing capability means the feature is unavailable and the client must remain usable
without it.

Required payload field: `capabilities`. Each capability requires `id` and `version`.

Both endpoints send `capabilities`. No capability is currently registered -- this is a separate,
still-empty registry, independent of the registered state areas below; both the host and the client
send an empty list, and any non-empty list is rejected as `unsupported_capability`.

### `subscribe`

Replaces the client's complete desired set of public state-area subscriptions after capabilities
are negotiated. Every request is authoritative for that connection: accepted areas omitted from a
later request stop receiving new Snapshots and Events. `stateAreas: []` removes every active
subscription for the connection. Repeating the same set is idempotent. This complete-set meaning is
incompatible with released Host `0.4.0`'s additive behavior; the Phase 5.3 contract requires the
next compatible Host minor line, `0.5.x`, as specified in
`ai/context/protocol/compatibility.md`.

```json
{
  "stateAreas": ["example_area"]
}
```

The host confirms the subscription and sends a `state_snapshot` before sending events only for a
requested state area that is registered and accepted. When every requested area is rejected, the
host sends only `subscription_ack` and no snapshot. An accepted area with no authoritative value
available yet is never a dead end: its baseline is delivered automatically, still correlated to the
`subscribe` message, as soon as one becomes available, or answered with a `temporarily_unavailable`
`error` if none does before a bounded deadline elapses.

Required payload field: `stateAreas`. The Host responds with `subscription_ack`. A requested area
that is one of the five registered state areas above is accepted; any other requested area is
rejected into `subscription_ack.rejectedStateAreas`. The resulting active set is exactly the
accepted areas from this request, so omitted previously accepted areas and areas rejected in this
request are removed from the active set. Duplicate entries are treated as one requested area.

### `subscription_ack`

Confirms accepted and rejected state areas:

```json
{
  "acceptedStateAreas": [],
  "rejectedStateAreas": ["example_area"]
}
```

Both arrays are required. The host sends snapshots only for accepted areas. A requested area among
the five registered state areas above appears in `acceptedStateAreas`; any other requested area
appears in `rejectedStateAreas`.

`subscription_ack.correlationId` is the `messageId` of the `subscribe` it answers. An accepted
area's own baseline snapshot correlates to whichever request is currently establishing it: the
`subscribe` message ID for its first baseline, or a later `snapshot_request`'s message ID when that
request is what triggered re-establishing it. An automatic re-baseline the Host starts on its own --
after a play-context transition, a state-authority rotation, or a bounded recovery buffer
overflowing -- is not a fresh client request; it reuses whichever message ID (the original
`subscribe`, or the most recent `snapshot_request`) that area was already correlated to, rather than
inventing a new one or always attaching to `snapshot_request`. Once an area is live and simply
receives a newer authoritative value outside of any recovery, that snapshot is unsolicited and its
`correlationId` is `null`, the same as an Event's.

### `snapshot_request`

Requests a fresh baseline for one registered state area. When the area is accepted, the host
responds with a `state_snapshot` at the current revision; an unregistered area is rejected instead.

```json
{
  "stateArea": "example_area",
  "knownRevision": 41
}
```

Required payload field: `stateArea`. `knownRevision` is optional and advisory only. An unregistered
area is rejected as `unsupported_capability`. A registered area never silently receives no response:
if an authoritative value is available, the `state_snapshot` is returned immediately; otherwise the
request is retained and answered automatically, still correlated to this `snapshot_request`, as soon
as a value becomes available, or with a `temporarily_unavailable` `error` if none does before a
bounded deadline elapses. A later `snapshot_request` for the same still-pending area supersedes an
earlier one rather than queuing a second reply.

### `state_snapshot`

Contains the complete state for one subscribed state area at a revision.

Required payload fields: `stateArea`, `revision`, `occurredAt`, `data`.

See `subscription_ack` above for exactly which request a given snapshot's `correlationId` reflects: a first baseline, a baseline re-established by an explicit `snapshot_request`, an automatic Host-initiated re-baseline, or an unsolicited live update.

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
`rate_limited`, `temporarily_unavailable`, and `internal_error`. `revoked` is a
`trusted_device_credential` hello rejected because the presented `clientId` was explicitly revoked,
distinct from `unauthenticated`'s "never paired or wrong credential" per
`ai/context/protocol/security.md`'s "Persistent local trust". `blocked` is an `unpaired` or
`trusted_device_credential` hello rejected because the presented `clientId` is a currently blocked
Known Device -- distinct from `revoked` (blocking prevents both authentication and re-pairing, while
a revoked device may still re-pair) and never issued for `one_time_local_token` (developer-token)
authentication, which stays a separate provider unaffected by Known Device blocking.
`temporarily_unavailable` is always `retryable: true`: a registered state area's authoritative
baseline was not available before its own bounded deadline elapsed (see `snapshot_request` and
`subscribe` above) -- a temporary Host-side readiness gap, never a client-caused violation. Error
codes are for branching; diagnostic messages are not. There is no Host-version-incompatibility wire
error code: a client detects incompatibility itself from `hello_ack.hostVersion` and fails without
completing the rest of the exchange, per `ai/context/protocol/compatibility.md`.

If no session has been established on a socket, an `error`'s `sessionId` is `null`; this includes
authentication failures and violations detected before decoding completes. After a successful
`hello_ack`, errors carry the active session identity.

### `session_invalidated`

An unsolicited, Host-originated terminal event for an authenticated session that an administrator
deliberately invalidated. It is sent best-effort before the Host force-closes the affected socket;
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
2. The host authenticates the client and replies with `hello_ack`, which exposes the host's
   release version (the `hostVersion` wire field) for the client to evaluate against its
   own declared supported range. The host
   does not reject a connection on compatibility grounds; it does not receive or evaluate a
   client-declared version range itself. A client that finds the exposed version outside its
   supported range fails explicitly on its own side rather than continuing the exchange.
3. They exchange `capabilities`.
4. The client sends `subscribe` and receives `subscription_ack`.
5. The host sends a snapshot before events for each accepted state area.
6. Each authenticated socket receives a unique `sessionId`. The session is bound exclusively to
   that socket and is invalidated when the socket closes for any reason; an administrative
   `session_invalidated` event may be sent before the force-close, but delivery is best-effort.
7. A session cannot be transferred, resumed, or reused on another socket. A reconnect creates a
   new session, and messages carrying an invalidated or foreign session ID are rejected as
   `stale_session` before application handling.
8. The client must not apply messages from its previous session. Queued state from that session is
   not replayed; a fresh snapshot establishes each new
   baseline.
9. During snapshot recovery, events are buffered or withheld by the host until the snapshot baseline is established; the client never guesses the cutoff.
10. A revision gap or queue-loss recovery requires a new `snapshot_request` before the state is presented as current; duplicate or stale events at or below the current revision are ignored.

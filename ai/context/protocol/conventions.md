# Protocol conventions

The protocol is the canonical contract joining the SKSE bridge and Flutter client. Its sole source of truth is `protocol/schema/`; shared contract fixtures live in `protocol/fixtures/`. It is not a shared implementation library and must not contain Flutter, Dart, C++, CommonLib, Redux, or Skyrim runtime types.

## Ownership model

```text
SKSE application values
          ↓
SKSE protocol adapter
          ↓
canonical DovahLink message
          ↓
Flutter protocol adapter
          ↓
Flutter client model
```

- The protocol owns message meaning, required fields, optional fields, and compatibility rules.
- SKSE owns native response types and maps them into protocol messages.
- Flutter owns client models and maps protocol messages into them.
- A Flutter model and an SKSE response may represent the same concept, but they are not interchangeable types.
- A protocol change must be understandable and testable from both sides.

## Message categories

Use the correct category instead of treating every message as a generic response:

- **Command:** a client asks the bridge to do something. The first release should have none that change game state.
- **Response:** the direct result of a command, including success or failure.
- **Snapshot:** a complete current view of a state area that can rebuild a client after connection or recovery.
- **Event:** an ordered update describing a change since a known state.
- **Capability:** what a connected endpoint supports.
- **Connection:** hello, pairing, heartbeat, reconnect, and protocol negotiation messages.

## Message design

- Keep messages small, explicit, and purpose-specific.
- Prefer neutral names such as `CharacterStateSnapshot` over names tied to Flutter or SKSE.
- Every message follows the concrete envelope in `protocol/schema/README.md`: `protocolVersion`, `messageType`, `messageId`, `sessionId`, `correlationId`, and `payload`.
- State messages contain `stateArea`, `revision`, and `occurredAt`; events additionally contain `baseRevision`.
- The schema document is authoritative when this convention summary and an implementation disagree.
- State whether a field is required, optional, nullable, or version-gated.
- Use explicit units, coordinate systems, enum meanings, and timestamp semantics.
- Do not encode presentation concerns such as widget layout, theme, or screen position into game-state messages.
- Do not use plausible defaults when the bridge does not know a value; represent unavailable data explicitly.

## State flow

- A client must be able to request or receive a fresh snapshot after reconnecting.
- Events must identify the state area they update and the sequence or revision they belong to.
- A client must be able to detect stale, duplicated, missing, and out-of-order updates.
- Event delivery must be safe to repeat or the contract must define why it is not.
- A snapshot supersedes older events for the same state area.
- A snapshot's revision is the baseline for subsequent events; events with a revision at or below the snapshot revision are ignored as stale or duplicate.
- Revisions reset per state area when `sessionId` changes; clients never compare revisions across sessions.
- The bridge serializes snapshot publication and event publication per state area so an event cannot be ambiguously before or after the snapshot baseline. The concrete recovery sequence is defined in `protocol/schema/README.md`.

## Initial message set

The first contract should start with only these conceptual messages:

- `hello` — endpoint identity and supported protocol versions
- `capabilities` — registered state capabilities
- `hello_ack` — selected protocol version after pre-negotiation
- `snapshot_request` — request a fresh state baseline
- `subscription_ack` — accepted and rejected state areas
- `state_snapshot` — complete state for one state area
- `state_event` — ordered change after a known revision
- `error` — structured connection or protocol failure
- `ping` / `pong` — connection liveness

Capability identifiers and state-area identifiers are canonical protocol values. They are not Dart class names, C++ enum names, or widget names.

## Boundary rules

- Serialization and deserialization happen at the protocol adapters, not inside game-state extraction or Flutter widgets.
- Transport framing is separate from message meaning.
- Authentication, pairing, and transport errors must not be confused with game-state errors.
- Unknown message types and optional fields must fail safely without corrupting known state.

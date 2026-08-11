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
- Every message follows the envelope and registered message shape in `protocol/schema/README.md`.
- State whether a field is required, optional, nullable, or version-gated.
- Use explicit units, coordinate systems, enum meanings, and timestamp semantics.
- Do not encode presentation concerns such as widget layout, theme, or screen position into game-state messages.
- Do not use plausible defaults when the bridge does not know a value; represent unavailable data explicitly.

## State flow

- State flow follows the session, revision, ordering, and recovery rules in
  `protocol/schema/README.md`; side-specific adapters do not reinterpret them.
- New message designs must let clients detect unavailable, stale, duplicated, missing, and
  out-of-order state without guessing.
- Capability, message, and state-area identifiers are canonical protocol values, not Dart class
  names, C++ enum names, or widget names.

## Boundary rules

- Serialization and deserialization happen at the protocol adapters, not inside game-state extraction or Flutter widgets.
- Flutter protocol adapters may use generated `json_serializable` models, but generated mapping never replaces the canonical schema or handwritten semantic validation at the client boundary.
- Transport framing is separate from message meaning.
- Authentication, pairing, and transport errors must not be confused with game-state errors.
- Unknown message types and optional fields must fail safely without corrupting known state.

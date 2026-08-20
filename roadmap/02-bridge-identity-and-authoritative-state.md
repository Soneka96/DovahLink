# Stage 2 — Bridge Identity and Authoritative State

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./01-skyrim-bridge-foundation.md) · [Next stage](./03-local-device-pairing-and-reconnection.md)

## 2. Bridge Identity and Authoritative State Foundation

**Status:** Complete

### Outcome

The implemented protocol and both sides of the connection adopt the identity, state-ownership, and
revision semantics defined in `ARCHITECTURE.md` while the protocol surface is still small.

### Scope and behavior

- Add the approved `bridgeInstanceId`, `playContextId`, `clientId`, and socket-bound `sessionId`
  lifetimes to the canonical contract and adapters.
- Define authoritative state identity as one `bridgeInstanceId`, `playContextId`, and state area;
  a bridge restart creates a new state identity even when the same play context remains loaded.
- Keep `sessionId` scoped to authenticated socket delivery only; reconnecting creates a new session
  without resetting the current authoritative revision.
- Make state revisions belong to that authoritative bridge instance, state area, and play context
  rather than one socket session.
- Advance a revision only when authoritative state changes; unchanged snapshot requests reuse it.
- Invalidate prior state when a new play context replaces the previous loaded game.
- Add an executable bridge-restart acceptance test proving cached state from the previous bridge
  lifetime is rejected, including when its play context and revision match the new bridge's values.
- Keep the independent validation client as proof that the protocol has no Flutter-only behavior.
- Do not silently reinterpret messages from the previously published experimental release as
  already carrying this ownership; that release is archived, not a supported compatibility target.
- Update the schema, fixtures, bridge, validation client, Flutter consumer, and tests together as
  one canonical contract change, not as a separate protocol generation kept alongside an older one.
- Identify compatibility by the DovahLink Bridge/mod release version rather than an independent
  protocol-generation number, per `ai/context/protocol/compatibility.md`.

### Dependencies and boundaries

This phase implements the target semantics in `ARCHITECTURE.md`. It does not add a separate
game-process identifier, concurrent sockets, LAN exposure, new state areas, or gameplay commands.

### Acceptance criteria

Messages identify the correct bridge, play context, client, and socket session; bridge restarts,
reconnects, and loaded save changes cannot make old state current; reconnects do not reset the
authoritative revision; unchanged snapshots do not manufacture revisions; the bridge-restart
acceptance test rejects prior-lifetime cached state; and official and independent clients pass the
canonical contract tests.

# Stage 9 — Multi-Client Runtime Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./08-live-player-state.md) · [Next stage](./10-multi-bridge-and-local-discovery-foundation.md)

## 9. Multi-Client Runtime Foundation

**Status:** Planned

### Outcome

One bridge serves concurrent clients while capture remains shared and one slow client cannot
destabilize Skyrim or healthy clients.

### Scope and behavior

- Replace the one-client slot with a bounded concurrent-client registry.
- Give each client independent session, capabilities, subscriptions, queues, recovery, and diagnostics.
- Fan out shared authoritative state without repeated Skyrim reads.
- Keep revisions shared while delivery state remains client-specific.
- Disconnect or resynchronize slow consumers without blocking others.
- Keep navigation and layout local to each client.
- Give the official Flutter client no privileged treatment.

### Dependencies and boundaries

This phase follows the Phase 8 single-client proof and remains loopback-only. It excludes LAN,
synchronized layouts, accounts, collaboration, and control permissions.

### Acceptance criteria

At least two clients receive consistent state and recover independently; client count does not
multiply equivalent Skyrim reads; and a slow client does not stall a healthy client.

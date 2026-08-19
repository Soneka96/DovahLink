# Stage 10 — Multi-Bridge and Local Discovery Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./09-multi-client-runtime-foundation.md) · [Next stage](./11-automatic-connection-and-transport-selection.md)

## 10. Multi-Bridge and Local Discovery Foundation

**Status:** Planned

### Outcome

Multiple bridge processes coexist on one machine without port collisions or ambiguous selection.

### Scope and behavior

- Treat the Phase 1 port as a preference rather than identity.
- Select another local port when the preferred port is occupied.
- Publish a small non-secret same-machine discovery record for each bridge.
- Validate records against authenticated `bridgeInstanceId` and tolerate stale records.
- Remove records on clean shutdown and isolate records owned by other instances.
- Let official and independent clients enumerate the same discovery surface.
- Avoid a resident machine-level discovery service.

### Dependencies and boundaries

This phase depends on Phases 2 and 9. LAN discovery belongs to Phase 22.

### Acceptance criteria

Two harness bridges run concurrently without manual port editing, appear as distinct choices, accept
independent clients, and cannot corrupt each other's discovery state.

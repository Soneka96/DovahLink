# Stage 10 — Multi-Instance and Local Discovery Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./09-multi-client-runtime-foundation.md) · [Next stage](./11-automatic-connection-and-transport-selection.md)

## 10. Multi-Instance and Local Discovery Foundation

**Status:** Planned

### Outcome

Multiple DovahLink instances coexist on one machine without port collisions or ambiguous selection.

### Scope and behavior

- Treat the Phase 1 port as a preference rather than identity; the early Stage 5A path may use
  automatic OS-selected ports, while this phase establishes the generalized multi-instance policy.
- Select another local port when the preferred port is occupied, or use an OS-selected port when no
  deterministic port is required.
- Publish a small non-secret same-machine discovery record for each instance.
- Validate records against authenticated `bridgeInstanceId` and tolerate stale records.
- Remove records on clean shutdown and isolate records owned by other instances.
- Let official and independent clients enumerate the same discovery surface.
- Avoid a resident machine-level discovery service.

### Dependencies and boundaries

This phase depends on Phases 2 and 9. Stage 5A proves a narrow single-client Android/LAN discovery
path early; this phase remains responsible for generalized same-machine multi-instance coordination.
Generalized secure LAN discovery belongs to Stage 22.

### Acceptance criteria

Two harness instances run concurrently without manual port editing, appear as distinct choices,
accept independent clients, and cannot corrupt each other's discovery state.

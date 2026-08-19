# Stage 11 — Automatic Connection and Transport Selection

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./10-multi-bridge-and-local-discovery-foundation.md) · [Next stage](./12-mod-awareness.md)

## 11. Automatic Connection and Transport Selection

**Status:** Planned

### Outcome

The client chooses the best path to the intended local bridge while preserving manual control.

### Scope and behavior

- Keep connection candidates separate from bridge identity.
- Remember the preferred bridge independently from its endpoint.
- Prefer approved same-machine candidates and retain manual fallback.
- Observe coarse Skyrim process presence without reading game memory.
- Represent stopped, launching, initializing, ready, loading, playing, recovery, and failures.
- Retry transient failures with bounded backoff and stop on terminal conditions.
- Let later LAN candidates join the same policy without a second connection architecture.

### Dependencies and boundaries

This phase consumes Phase 10 and remains loopback-only. It excludes a resident monitor, LAN access,
internet exposure, hosted relay, and accounts.

### Acceptance criteria

The client reconnects to the intended bridge, never confuses simultaneous instances, backs off
responsibly, and provides actionable manual recovery.

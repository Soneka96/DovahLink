# Stage 1 — Skyrim Bridge Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./00-foundation.md) · [Next stage](./02-bridge-identity-and-authoritative-state.md)

## 1. Skyrim Bridge Foundation

**Status:** Complete

### Outcome

Skyrim can expose a minimal, trustworthy state value to one external validation client through a DovahLink-owned connection boundary.

### Scope and behavior

- Confirm the first supported Skyrim edition and development environment.
- Evaluate SkyrimWebSocket as the reference and reusable foundation instead of rebuilding an equivalent native bridge from scratch.
- Record which parts can be adopted and which must be adapted or excluded to meet DovahLink's runtime, lifecycle, protocol, security, and repository boundaries.
- Keep SkyrimWebSocket-specific types and wire behavior behind DovahLink-owned game-state, application, protocol-mapping, and transport boundaries.
- Use the canonical DovahLink protocol over the approved loopback-only connection.
- Connect one external validation client, show connection state, and display one trustworthy value.
- Document setup, supported versions, failure behavior, and known limitations.

### Dependencies and boundaries

This phase depends on the documentation baseline and the completed Phase 0.5 protocol-facing
foundation. Its external validation client is a separate proof client that speaks the canonical
protocol; this phase does not turn the Phase 0.5 Flutter shell into the connected product client or
create map resources, a UI theme system, remote-device networking, LOTD data, or unrelated bridge
capabilities.

### Acceptance criteria

The reuse decisions are documented, the bridge follows the approved lifecycle and protocol
boundaries, one external validation client can authenticate and display the chosen value without
presenting stale data as current, a connection attempt that fails before the one-time token is
consumed can retry and still succeed, and setup is reproducible. Reconnecting a client that has
already completed one successful session without restarting the bridge is a known Phase 1
limitation addressed by Phase 3 and is not part of this phase's acceptance bar.

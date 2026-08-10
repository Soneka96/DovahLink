# Architecture

## Current state

There is no implementation yet. This document records the initial boundaries so the first feature can stay small and replaceable.

## Repository boundaries

The future implementation is divided into explicit areas:

```text
app/          Flutter client
bridge/       native SKSE bridge
protocol/     canonical cross-side schemas and shared fixtures
integration/  cross-area tests and scenarios
ai/context/   AI development conventions
```

These are ownership boundaries, not folders to pre-create. Add an area when its first real file is needed. Protocol schemas and shared fixtures belong only in `protocol/`; client and bridge adapters consume them but do not redefine them.

## Target shape

```text
┌─────────────┐     ┌──────────────┐     ┌───────────────┐
│ Skyrim      │────▶│ Skyrim       │────▶│ DovahLink     │
│ game state  │     │ bridge       │     │ protocol      │
└─────────────┘     └──────────────┘     └───────┬───────┘
                                                  │
                                    ┌─────────────┴─────────────┐
                                    │                           │
                              ┌─────▼─────┐               ┌─────▼─────┐
                              │ Desktop   │               │ Mobile    │
                              │ client    │               │ client    │
                              └───────────┘               └───────────┘
```

## Boundaries

### Skyrim bridge

Reads the supported game state and exposes a small, versioned stream of events. It should not own presentation or device-specific behavior.

### Protocol

Defines the canonical connection, pairing, capability, state, and error contract between the bridge and clients. It is the seam between the two sides of one product, not a third implementation layer.

SKSE owns native response and application types. Flutter owns client models. Both map to and from the protocol contract; neither side's internal types become the contract.

### Clients

Render companion views and manage local layout preferences. A client should remain useful when optional data is unavailable.

## Initial technical decisions

- The repository will eventually contain a Flutter client and a Skyrim integration, but neither is created in this documentation phase.
- The bridge-to-client contract should be defined before multiple clients are built.
- The protocol contract is the source of truth for cross-side messages; Flutter and SKSE adapters must not silently invent incompatible fields.
- The Phase 1 reference encoding is UTF-8 JSON with one complete object per transport message; the transport must preserve those message boundaries.
- The first connection proof is same-machine and loopback-only; it must not expose a listening service to the LAN.
- LAN or remote-device support is not approved until `ai/context/protocol/security.md` is implemented and its required scenarios pass.

## Reliability expectations

- Treat the game as an unreliable producer: values may be unavailable or delayed.
- Make connection state visible to the player.
- Reject incompatible protocol versions clearly.
- Avoid allowing a stale value to look current.
- Keep the first client read-only.

## Architectural non-goals

- No service layer before local connectivity is proven.
- No shared abstraction for clients before there is a second client.
- No permanent protocol complexity for hypothetical features.

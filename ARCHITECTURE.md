# Architecture

## Current state

There is no implementation yet. This document records the initial boundaries so the first feature can stay small and replaceable.

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
- The first connection should work on a local network or the same machine without requiring a hosted backend.
- Transport, serialization, and pairing details remain open until the first connection experiment identifies the smallest reliable choice.

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

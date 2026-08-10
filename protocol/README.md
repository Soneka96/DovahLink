# DovahLink protocol

This directory is the sole source of truth for messages exchanged between the SKSE bridge and Flutter clients.

## Ownership

- `schema/` defines the canonical wire contract.
- `fixtures/` contains language-neutral examples used by both sides.
- `app/` and `bridge/` contain adapters, not competing protocol definitions.
- A Flutter model or SKSE response may map to a protocol message, but neither becomes the protocol.

The Phase 1 reference encoding is UTF-8 JSON: one complete JSON object per transport message. The transport must preserve message boundaries; framing is not part of the JSON payload.

## Current version

The first contract version is `1`. It is intentionally read-only and supports connection negotiation, capabilities, subscriptions, snapshots, events, errors, and liveness.

See [schema/README.md](schema/README.md) for the contract and [fixtures/README.md](fixtures/README.md) for shared examples.

# DovahLink protocol

This directory owns the contract between the SKSE bridge and DovahLink clients.

## Contents

- `schema/` is the sole source of truth for the current protocol version, wire format, registered
  messages, and session and recovery behavior.
- `fixtures/` contains language-neutral examples used by both sides.
- The repository-root [`app/`](../app/) and planned `bridge/` areas contain adapters, not competing
  protocol definitions. Their locations are defined by the root
  [repository boundaries](../ARCHITECTURE.md#repository-boundaries).
- A Flutter model or SKSE response may map to a protocol message, but neither becomes the protocol.

See [schema/README.md](schema/README.md) for the contract and [fixtures/README.md](fixtures/README.md) for shared examples.

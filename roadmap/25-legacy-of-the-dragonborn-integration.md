# Stage 25 — Legacy of the Dragonborn Integration

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./24-item-knowledge-and-search.md) · [Next stage](./26-installed-ui-detection.md)

## 25. Legacy of the Dragonborn Integration

**Status:** Planned

### Outcome

Supported LOTD setups relate item knowledge and live save state to museum progress.

### Scope and behavior

- Detect explicitly supported LOTD versions and required data.
- Add collection/display state and acquisition context only where reliable.
- Keep static museum knowledge distinct from live state.
- Explain unsupported versions, missing patches, replicas, and ambiguity.

### Dependencies and boundaries

LOTD is optional and cannot be required by inventory, knowledge, or map features. It does not modify
museum state.

### Acceptance criteria

Supported setups report verified state, unsupported setups fall back to ordinary knowledge, and
ambiguous values are not authoritative.

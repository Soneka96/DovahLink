# Stage 24 — Item Knowledge and Search

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./23-mobile-tablet-client.md) · [Next stage](./25-legacy-of-the-dragonborn-integration.md)

## 24. Item Knowledge and Search

**Status:** Planned

### Outcome

Players search versioned reference information without confusing it with live save truth.

### Scope and behavior

- Keep large reference data outside the live bridge stream.
- Provide approved names, categories, descriptions, guidance, and source context.
- Link entries to live state only through stable identities and explicit confidence.
- Make provenance, version, localization, caching, and updates visible.

### Dependencies and boundaries

Reference data cannot claim current ownership or location without authoritative state. LOTD remains
separate.

### Acceptance criteria

Search remains responsive, provenance is clear, and outdated knowledge cannot create false live-state
claims.

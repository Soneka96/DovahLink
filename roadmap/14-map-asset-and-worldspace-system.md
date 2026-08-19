# Stage 14 — Map Asset and Worldspace System

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./13-interactive-map-foundation.md) · [Next stage](./15-quests.md)

## 14. Map Asset and Worldspace System

**Status:** Planned

### Outcome

Map imagery and worldspaces grow without bloating live traffic or loading every map.

### Scope and behavior

- Define versioned client packages for maps, tiles, marker artwork, and slow-changing assets.
- Cache, validate, update, and invalidate packages deliberately.
- Lazy-load DLC and supported mod worldspaces.
- Investigate installed map derivation only where technically and legally appropriate.
- Define attribution, licensing, fetching, fallback, and incompatibility behavior.
- Keep live state separate from packages.

### Dependencies and boundaries

This phase extends Phase 13. Assets do not become live protocol messages.

### Acceptance criteria

Packages load independently from live state, additional worldspaces do not inflate ordinary traffic,
and missing assets cannot break synchronization.

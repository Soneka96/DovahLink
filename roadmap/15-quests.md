# Stage 15 — Quests

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./14-map-asset-and-worldspace-system.md) · [Next stage](./16-navigation-path-guidance.md)

## 15. Quests

**Status:** Planned

### Outcome

The player reviews active quests and objectives without opening the journal.

### Scope and behavior

- Present approved fields, ordering, completion, failure, and active state.
- Handle hidden, localized, modded, malformed, or rapidly changing data.
- Link objectives to markers only when trustworthy.
- Prefer event-driven reconciliation where reliable.

### Dependencies and boundaries

This read-only phase does not activate quests, change stages, repair quests, or infer hidden locations.

### Acceptance criteria

Representative quests remain accurate through loads and reconnects, and hidden information is not
fabricated.

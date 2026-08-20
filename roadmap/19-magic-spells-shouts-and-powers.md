# Stage 19 — Magic, Spells, Shouts, and Powers

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./18-equipment.md) · [Next stage](./20-favorites-and-hotkeys.md)

## 19. Magic, Spells, Shouts, and Powers

**Status:** Planned

### Outcome

The player browses learned abilities and current selection or usability state.

### Scope and behavior

- Separate spells, shouts, powers, and approved ability types.
- Present supported category, cost, cooldown, effect, selection, favorite, and hotkey metadata
  supplied by the owning capabilities; favorite state and hotkey assignment semantics belong to
  Stage 20.
- Handle mod-added abilities and incomplete metadata.
- Keep reference enrichment separate from live state.

### Dependencies and boundaries

This read-only phase excludes casting, equipping, unlocking, and spending resources.

### Acceptance criteria

Abilities appear in correct categories, selection and cooldown remain current, and unknown behavior
degrades without fabricated values.

# Stage 18 — Equipment

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./17-inventory.md) · [Next stage](./19-magic-spells-shouts-and-powers.md)

## 18. Equipment

**Status:** Planned

### Outcome

The client shows current equipment and its relationship to inventory.

### Scope and behavior

- Represent approved weapons, armor, ammunition, hands, voice, and supported slots.
- Handle dual wielding, two-handed items, shields, transformations, and reliable modded slots.
- Link equipment to stable inventory identity.
- Reconcile Skyrim and mod-driven changes promptly.

### Dependencies and boundaries

This read-only phase excludes equip, unequip, loadouts, and remote item actions.

### Acceptance criteria

Equipment matches authoritative state and cannot remain falsely equipped after invalidation.

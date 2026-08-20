# Stage 17 — Inventory

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./16-navigation-path-guidance.md) · [Next stage](./18-equipment.md)

## 17. Inventory

**Status:** Planned

### Outcome

The player browses and searches trustworthy read-only inventory.

### Scope and behavior

- Represent stable identity, count, category, weight, value, and approved metadata.
- Handle stacks, instances, renamed, enchanted, and mod-added items.
- Reconcile looting, trading, crafting, loading, and reconnect changes.
- Keep filtering client-side where practical.
- Keep enrichment separate from ownership state.

### Dependencies and boundaries

This phase does not equip, drop, consume, transfer, favorite, or modify items.

### Acceptance criteria

Inventory reconciles without stale duplicates, remains responsive at realistic sizes, and does not
repeatedly rescan during idle play without reason.

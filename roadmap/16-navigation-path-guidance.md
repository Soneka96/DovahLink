# Stage 16 — Navigation / Path Guidance

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./15-quests.md) · [Next stage](./17-inventory.md)

## 16. Navigation / Path Guidance

**Status:** Planned

### Outcome

The map shows route guidance only when Skyrim provides a trustworthy route.

### Scope and behavior

- Validate exposure of native Clairvoyance or Guide routes as lightweight polylines.
- Render and refresh Skyrim-calculated segments.
- Explain unavailable, partial, invalidated, or recalculating routes.
- Invalidate on play-context, worldspace, objective, or source changes.
- Measure runtime cost before selecting refresh behavior.

### Dependencies and boundaries

DovahLink does not own pathfinding, maintain a navmesh database, or present approximations as
authoritative. Arbitrary map destinations remain deferred.

### Acceptance criteria

A reliable native route renders and invalidates correctly; an unreliable route is explicitly
deferred.

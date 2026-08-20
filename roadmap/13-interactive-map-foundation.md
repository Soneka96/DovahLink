# Stage 13 — Interactive Map Foundation

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./12-mod-awareness.md) · [Next stage](./14-map-asset-and-worldspace-system.md)

## 13. Interactive Map Foundation

**Status:** Planned

### Outcome

A player follows live position and trustworthy marker state on a responsive companion map.

### Scope and behavior

- Establish viewport, pan, zoom, coordinate conversion, and responsive presentation.
- Track player position and direction as lightweight overlays.
- Present marker state only where reliable.
- Preserve unknown, discovered, cleared, and unavailable distinctions.
- Support the base worldspace first.
- Keep static resources separate from live overlays.

### Dependencies and boundaries

This phase depends on live location and the core theme. It excludes routes, arbitrary destinations,
every worldspace, and a parallel navmesh database.

### Acceptance criteria

The base map tracks accurately, handles worldspace and play-context changes, survives reconnects,
and never invents discovery or cleared state.

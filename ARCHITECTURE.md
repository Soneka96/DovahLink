# Architecture

## Repository boundaries

The future implementation is divided into explicit areas:

```text
app/          Flutter client
bridge/       native SKSE bridge
protocol/     canonical cross-side schemas and shared fixtures
integration/  cross-area tests and scenarios
ai/context/   AI development conventions
```

These are ownership boundaries, not folders to pre-create. Add an area when its first real file is needed. Protocol schemas and shared fixtures belong only in `protocol/`; client and bridge adapters consume them but do not redefine them.

## Target shape

```text
┌─────────────┐     ┌──────────────┐     ┌───────────────┐
│ Skyrim      │────▶│ Skyrim       │────▶│ DovahLink     │
│ game state  │     │ bridge       │     │ protocol      │
└─────────────┘     └──────────────┘     └───────┬───────┘
                                                  │
                                    ┌─────────────┴─────────────┐
                                    │                           │
                              ┌─────▼─────┐               ┌─────▼─────┐
                              │ Desktop   │               │ Mobile    │
                              │ client    │               │ client    │
                              └───────────┘               └───────────┘
```

## Boundaries

### Skyrim bridge

Reads the supported game state and exposes a small, versioned stream of events. It should not own presentation or device-specific behavior.

SkyrimWebSocket is the starting reference and bridge foundation, not a separate architecture for DovahLink to reproduce from scratch. The first bridge feature should identify the parts that can be reused, then adapt them behind DovahLink's game-state, application, protocol-mapping, and transport boundaries. Reuse does not make SkyrimWebSocket's internal types or wire format canonical: the DovahLink protocol remains the cross-side contract, and code that does not fit the runtime, security, lifecycle, or ownership rules should be replaced or left out deliberately.

### Protocol

Defines the canonical connection, pairing, capability, state, and error contract between the bridge and clients. It is the seam between the two sides of one product, not a third implementation layer.

SKSE owns native response and application types. Flutter owns client models. Both map to and from the protocol contract; neither side's internal types become the contract.

### Clients

Render companion views and manage local layout preferences. A client should remain useful when optional data is unavailable.

## Initial technical decisions

- The bridge-to-client contract should be defined before multiple clients are built.
- The protocol contract is the source of truth for cross-side messages; Flutter and SKSE adapters must not silently invent incompatible fields.
- The wire contract is defined by `protocol/schema/README.md`; transport framing remains outside
  the architecture contract.
- Transport exposure, pairing, authentication, and input limits are defined by
  `ai/context/protocol/security.md`.

## Reliability expectations

- Treat the game as an unreliable producer: values may be unavailable or delayed.
- Make connection state visible to the player.
- Reject incompatible protocol versions clearly.
- Avoid allowing a stale value to look current.
- Keep the first client read-only.

## Future feature boundaries

### Map data

A future map should separate expensive, slow-changing resources from small playthrough updates:

- Base maps, tiles, marker artwork, and other heavy resources are versioned and cached by the client rather than repeatedly streamed by the bridge.
- Additional worldspaces, DLC maps, and mod-provided map resources are loaded only when needed.
- Player position, active markers, discovery state, and Skyrim's native cleared state are lightweight snapshots or events layered over those resources.
- A location is shown as cleared only when Skyrim provides that state; DovahLink must not invent a universal completion percentage for locations that are not clearable.

Route guidance must reuse a path calculated by Skyrim, initially by validating whether the route produced by the native Clairvoyance/Guide system can be exposed safely as a lightweight polyline for the active quest objective. DovahLink may render and refresh that route, but it must not own a pathfinding engine, extract and maintain a parallel navmesh database, or approximate an unavailable route as valid. If Skyrim does not expose a reliable route, route guidance is deferred. Arbitrary map-selected destinations remain separate future work unless Skyrim can accept such a target through an explicitly approved safety model.

This split belongs to a dedicated map feature and does not expand the connection proof.

### Item knowledge and LOTD

A future item-knowledge feature may combine a searchable, versioned knowledge source with read-only save and load-order state. Optional Legacy of the Dragonborn (LOTD) support may add acquisition guidance, source quests or locations, supported-mod context, and collected or displayed state when those values can be determined reliably.

The knowledge source belongs outside the live bridge stream; the bridge should expose only relevant game state. This is separate from the map, bridge validation, and first companion workflow, even if a later client links an item result to a map location.

### UI theming and adaptation

The Flutter client's Core UI Theme System defines its Skyrim-inspired native presentation through
shared tokens and components for fonts, colors, panels, icons, spacing, and animations. Dashboard
modules consume that theme boundary rather than defining their own visual constants.

The core theme must remain usable without inspecting the Skyrim installation or depending on a
particular UI mod. Optional Installed UI Detection may identify supported UI and font resources
when detection is reliable, and optional adapters may map explicitly supported themes such as
SkyUI, Nordic UI, and Dear Diary into the DovahLink theme boundary. A `dovahlink-theme.json` format
may provide the same boundary for compatible theme packs, but it is not part of the core theme and
must be specified and approved before implementation.

Detection and adapters may supply supported presentation values; they must not replace client behavior, dashboard structure, or protocol models. Missing, unsupported, or invalid resources fall back to the native DovahLink theme.

## Architectural non-goals

- No service layer before local connectivity is proven.
- No shared abstraction for clients before there is a second client.
- No permanent protocol complexity for hypothetical features.
- No dependency on an installed Skyrim UI mod for the default client presentation.

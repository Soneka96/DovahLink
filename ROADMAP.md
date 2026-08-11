# Roadmap

The roadmap is intentionally feature-based. Each numbered phase should deliver one coherent player-facing capability or one necessary product foundation, and should be completed through a focused feature branch and pull request.

A roadmap phase describes the intended outcome, behavior, boundaries, dependencies, and acceptance criteria. It is not implementation authorization. Implementation begins only after a direct instruction from the maintainer in the current task explicitly names the phase and requested scope. Issues, prior conversations, suggestions, and `continue` messages do not authorize unrelated work.

The order records current product dependencies. A later phase may be refined as earlier work reveals constraints, but it should not be pulled forward silently or bundled into an earlier feature.

## 0. Documentation baseline

**Status:** Complete

### Outcome

DovahLink has a self-contained source of truth for product scope, architecture, protocol ownership, security constraints, development conventions, and maintainer authority before implementation begins.

### Scope and behavior

- Establish the product direction and initial architecture boundaries.
- Define the canonical protocol boundary and initial security constraints.
- Define the contribution, branch, review, and maintainer-approval workflow.
- Copy the AI development conventions needed for Flutter, SKSE, protocol, and integration work into this repository.
- Keep the repository implementation-free until the maintainer explicitly starts the first implementation phase.

### Dependencies and boundaries

This phase has no implementation dependency. It establishes direction and authority but does not approve or pre-create Flutter, bridge, networking, protocol, or feature code.

### Acceptance criteria

The required documentation exists in the repository, agrees on ownership and safety boundaries, and does not depend on instructions from another local project. Later phases still require direct maintainer authorization.

## 0.5 Client and Protocol Foundation

**Status:** Complete

### Outcome

DovahLink has the smallest replaceable Flutter and protocol-facing foundation needed to begin
bridge integration without mixing client structure with transport implementation.

### Scope and behavior

- Establish the Flutter client shell and manual dependency-injection boundary.
- Generate and validate the first protocol-facing client models.
- Define connection domain entities, repository contracts, and use cases.
- Define Redux connection state, actions, reducers, selectors, and a read-only status screen.
- Establish client conventions, fixtures, generated-code rules, and test coverage for this
  foundation.

### Dependencies and boundaries

This foundation does not implement Skyrim integration, a transport, pairing, reconnection, or an
external validation client. It prepares those boundaries without claiming that a connection works.

### Acceptance criteria

The Flutter project analyzes cleanly, its foundation tests pass, protocol models map the approved
fixtures, and the client can render explicit disconnected and connection-error states without
real transport access.

## 1. Skyrim Bridge Foundation

**Status:** Next

### Outcome

Skyrim can expose a minimal, trustworthy state value to one external validation client through a DovahLink-owned connection boundary.

### Scope and behavior

- Confirm the first supported Skyrim edition and development environment.
- Evaluate SkyrimWebSocket as the reference and reusable foundation instead of rebuilding an equivalent native bridge from scratch.
- Record which parts can be adopted and which must be adapted or excluded to meet DovahLink's runtime, lifecycle, protocol, security, and repository boundaries.
- Keep SkyrimWebSocket-specific types and wire behavior behind DovahLink-owned game-state, application, protocol-mapping, and transport boundaries.
- Use the canonical DovahLink protocol over the approved loopback-only connection.
- Connect one external validation client, show connection state, and display one trustworthy value.
- Document setup, supported versions, failure behavior, and known limitations.

### Dependencies and boundaries

This phase depends only on the documentation baseline. It does not create the Flutter product client, map resources, UI theme system, remote-device networking, LOTD data, or unrelated bridge capabilities.

### Acceptance criteria

The reuse decisions are documented, the bridge follows the approved lifecycle and protocol boundaries, one external validation client can reconnect and display the chosen value without presenting stale data as current, and setup is reproducible.

## 2. PC / Second-Screen Baseline

**Status:** Planned

### Outcome

A player can run the first native Flutter DovahLink client on a PC or second screen and understand whether it is connected to Skyrim.

### Scope and behavior

- Create the first Flutter product client for desktop-sized layouts.
- Show a small read-only connection and status view using the protocol proven in Phase 1.
- Represent connecting, connected, recovering, incompatible, unavailable, and disconnected states clearly.
- Keep the application usable when Skyrim is not running or optional data is missing.
- Establish the smallest approved client structure needed for later feature screens without pre-creating speculative layers.

### Dependencies and boundaries

This phase consumes the Phase 1 bridge and protocol contract. It does not add automatic discovery, remote gameplay actions, mobile packaging, a customizable dashboard, or the final Skyrim-inspired visual system.

### Acceptance criteria

The desktop client starts independently, connects to the supported local bridge, shows trustworthy connection and sample state, recovers from an interrupted connection, and explains actionable failures without requiring developer documentation.

## 3. Automatic Connection System

**Status:** Planned

### Outcome

The normal local setup connects with minimal player configuration while remaining predictable and secure.

### Scope and behavior

- Detect or locate the approved same-machine bridge endpoint without requiring the player to enter transport details on every launch.
- Remember only safe local connection preferences.
- Distinguish bridge unavailable, Skyrim unavailable, version mismatch, recovery, and configuration errors.
- Retry with bounded backoff and provide an explicit manual retry path.
- Preserve protocol compatibility checks during every new or recovered session.

### Dependencies and boundaries

This phase builds on the bridge and desktop client. It remains loopback-only and does not authorize LAN discovery, internet exposure, hosted relay services, accounts, or silent weakening of pairing and security requirements.

### Acceptance criteria

A supported local installation connects automatically during the normal startup flow, recovers after either side restarts, stops retrying responsibly, and gives the player enough information to correct a failed setup.

## 4. Live Player State

**Status:** Planned

### Outcome

The client becomes useful during play by presenting a focused set of current character information.

### Scope and behavior

- Define the first approved set of high-value player values, such as identity, level, health, magicka, stamina, location, or combat state.
- Expose only values that can be sourced reliably from the supported Skyrim runtime.
- Define a stable, versioned protocol slice for snapshots and updates.
- Mark unavailable, delayed, recovering, and stale values honestly.
- Refresh efficiently without coupling presentation timing to the game thread.

### Dependencies and boundaries

This phase extends the established bridge, protocol, and client boundaries. It is read-only and does not include inventory management, equipment changes, map navigation, or arbitrary remote commands.

### Acceptance criteria

The agreed values update accurately during realistic play, session replacement cannot leak old values into the current view, unavailable data degrades clearly, and focused integration scenarios verify the bridge and client agree on the contract.

## 5. Mod Awareness

**Status:** Planned

### Outcome

DovahLink can describe the supported modded environment well enough for later features to adapt safely without pretending every load order is identical.

### Scope and behavior

- Identify the minimum load-order, plugin, and capability information needed by approved features.
- Expose capabilities rather than scattering mod-name checks through the client.
- Treat missing, unsupported, or ambiguous mod information as unavailable rather than guessing.
- Keep compatibility knowledge versioned and independently maintainable where practical.
- Document privacy, performance, and failure considerations for inspecting installed or active content.

### Dependencies and boundaries

This phase does not add mod-specific UI themes, LOTD behavior, a general plugin platform, or promises of compatibility with every mod. Later integrations must declare exactly which detected capability they consume.

### Acceptance criteria

The client can display a trustworthy summary of approved capabilities for representative clean and modded load orders, unsupported cases fall back safely, and no later feature needs to consume raw implementation-specific bridge details.

## 6. Interactive Map Foundation

**Status:** Planned

### Outcome

A player can follow their current position and useful Skyrim map state on a responsive second-screen map.

### Scope and behavior

- Establish the map viewport, pan and zoom behavior, coordinate conversion, and responsive presentation.
- Show player position and approved live markers as lightweight overlays.
- Preserve the distinction between unknown, discovered, and Skyrim-native cleared state.
- Support the base worldspace first and define how unavailable worldspaces are communicated.
- Keep slow-changing map resources separate from live playthrough updates.

### Dependencies and boundaries

This phase depends on live player state and the protocol slice for location updates. It does not include route calculation, arbitrary destination commands, every DLC or mod worldspace, or a parallel navmesh database.

### Acceptance criteria

The supported base map tracks the player accurately, remains usable at intended desktop sizes, handles transitions and missing coordinates safely, and never invents completion or cleared state.

## 7. Map Asset System

**Status:** Planned

### Outcome

Map imagery and marker artwork can grow without bloating the live bridge stream or forcing all worldspaces into memory.

### Scope and behavior

- Define versioned client-side packages for base maps, tiles, marker artwork, and other slow-changing resources.
- Cache installed resources and invalidate incompatible versions deliberately.
- Load additional worldspaces, DLC maps, and supported mod-provided resources only when needed.
- Define attribution, licensing, validation, and fallback requirements for distributed assets.
- Keep player, marker, discovery, and native cleared state as separate lightweight overlays.

### Dependencies and boundaries

This phase extends the map foundation. Asset packages do not become protocol messages, and the bridge does not repeatedly stream heavy images or tiles.

### Acceptance criteria

The client can install, validate, cache, update, lazy-load, and fall back from supported map packages without breaking the live map or requiring Skyrim to resend static resources.

## 8. Quests

**Status:** Planned

### Outcome

The player can review active quest information and understand which objective is currently relevant without opening the in-game journal.

### Scope and behavior

- Present approved active quest and objective fields that Skyrim exposes reliably.
- Preserve ordering, completion, failure, and active-objective state where available.
- Handle hidden, modded, malformed, or rapidly changing quest data without blocking the rest of the client.
- Link objectives to map markers only when a trustworthy relationship exists.
- Keep text encoding and localization behavior explicit.

### Dependencies and boundaries

This phase is read-only. It does not activate quests, select objectives, advance stages, repair broken quests, or infer undisclosed objective locations.

### Acceptance criteria

Representative base-game and modded quests display current objectives accurately, changes recover across loads and reconnects, and missing or hidden information is not fabricated.

## 9. Navigation / Path Guidance

**Status:** Planned

### Outcome

The map can render route guidance for the active quest objective when Skyrim can provide a trustworthy route.

### Scope and behavior

- Validate whether the native Clairvoyance or Guide system can expose a safe, lightweight route polyline.
- Render and refresh Skyrim-calculated route segments on the existing map.
- Explain unavailable, partial, invalidated, or recalculating routes.
- Discard routes when the session, worldspace, objective, or source calculation changes.
- Measure runtime cost before making route refresh frequent.

### Dependencies and boundaries

DovahLink may display a route but must not own a pathfinding engine, extract and maintain a parallel navmesh database, or present an approximation as valid. Arbitrary map-selected destinations remain deferred unless Skyrim can accept them through a separately approved safety model.

### Acceptance criteria

The feasibility decision is documented. If the native route is reliable, the client displays and invalidates it correctly for supported scenarios. If it is not reliable, the feature is explicitly deferred rather than replaced with misleading guidance.

## 10. Inventory

**Status:** Planned

### Outcome

The player can browse and search a trustworthy read-only view of their current inventory on the companion screen.

### Scope and behavior

- Present stable item identity, name, count, category, weight, value, and other approved fields.
- Handle stacks, instances, renamed items, enchanted items, mod-added items, and unavailable metadata deliberately.
- Provide responsive filtering, sorting, and search without requiring repeated full-state traffic.
- Reconcile inventory changes after trading, crafting, looting, loading, or reconnecting.
- Keep display enrichment separate from authoritative live inventory state.

### Dependencies and boundaries

This phase does not equip, drop, consume, transfer, favorite, or modify items. Those actions require separate authorization and safety design.

### Acceptance criteria

Inventory contents reconcile accurately across representative gameplay changes and modded items, duplicate or stale entries do not persist after recovery, and filtering remains responsive for realistically large inventories.

## 11. Equipment

**Status:** Planned

### Outcome

The client clearly shows what the character currently has equipped and how those items relate to the inventory.

### Scope and behavior

- Represent equipped weapons, armor, ammunition, and other approved slots.
- Handle dual wielding, two-handed items, shields, transformed states, and modded slot usage where reliable.
- Link equipment entries to inventory identity without assuming display names are unique.
- Update promptly when Skyrim or another mod changes equipment.
- Fall back to a clear unknown state when slot ownership cannot be resolved.

### Dependencies and boundaries

This phase is a read-only view built on inventory identity. Equipping, unequipping, loadouts, and remote item actions are deferred.

### Acceptance criteria

The view matches Skyrim across supported equipment transitions, recovers after loads and transformations, and does not show an item as equipped after its authoritative state is lost.

## 12. Magic, Spells, Shouts, and Powers

**Status:** Planned

### Outcome

The player can browse learned magical abilities and understand their current usability and selection state.

### Scope and behavior

- Separate spells, shouts, powers, and other supported abilities into understandable views.
- Present learned state, school or category, resource cost, cooldown, selected hand or voice slot, and descriptive metadata only where reliable.
- Handle mod-added abilities and missing metadata without breaking the feature.
- Reconcile selection and cooldown changes from authoritative Skyrim state.
- Keep rich reference information separate from live state.

### Dependencies and boundaries

This phase is read-only. Casting, equipping, unlocking, or spending resources from the companion client is not included.

### Acceptance criteria

Supported abilities appear in the correct category, live selection and cooldown state remain current, and unknown mod-added behavior degrades without fabricated values.

## 13. Favorites and Hotkeys

**Status:** Planned

### Outcome

The player can review the in-game favorites and hotkey arrangement from the companion screen.

### Scope and behavior

- Show which supported items and abilities are favorited.
- Represent native hotkey assignments and conflicts where Skyrim exposes them reliably.
- Link favorite entries to the corresponding inventory, equipment, or magic identity.
- Reconcile changes made in-game or by supported mods.
- Explain entries that exist but cannot be fully resolved.

### Dependencies and boundaries

This phase does not activate hotkeys, use items, cast abilities, or replace mod-specific favorites-menu behavior.

### Acceptance criteria

Favorites and assignments match authoritative game state through normal changes and reconnects, and unresolved entries remain visible without being misidentified.

## 14. Core UI Theme System

**Status:** Planned

### Outcome

DovahLink gains a coherent Skyrim-inspired identity before customizable dashboard layouts multiply its visual surface area.

### Scope and behavior

- Build the default presentation with native Flutter theming and components.
- Define shared theme values for fonts, colors, panels, icons, spacing, corner treatment, elevation, and animations.
- Apply those values consistently across existing screens and reusable states such as loading, empty, unavailable, stale, and error views.
- Support responsive sizing, accessibility, reduced motion, and readable contrast without losing the intended identity.
- Design the theme boundary so future adapters can supply approved values without controlling application behavior.

### Dependencies and boundaries

The native DovahLink theme must be complete without inspecting the Skyrim installation or requiring a UI mod. Installed UI Detection, SkyUI, Nordic UI, Dear Diary, custom theme packs, and `dovahlink-theme.json` are deliberately later phases.

### Acceptance criteria

All existing client surfaces use shared theme tokens or themed components, repeated visual constants are removed from feature widgets, supported screen sizes and accessibility settings remain usable, and missing optional resources cannot prevent the default interface from rendering.

## 15. Customizable Dashboard

**Status:** Planned

### Outcome

Players can choose which approved companion modules are visible and arrange them for their own second-screen workflow.

### Scope and behavior

- Provide movable and resizable modules for features already delivered by earlier phases.
- Support practical desktop and second-screen grid constraints rather than unrestricted layouts that can become unusable.
- Save, restore, reset, and migrate local layout preferences.
- Define minimum sizes, empty states, overflow behavior, and responsive adaptations for every module.
- Keep information useful when one module or optional data source is unavailable.

### Dependencies and boundaries

This phase depends on the Core UI Theme System and reuses existing feature views. It does not include cloud-synchronized layouts, arbitrary third-party widgets, or multi-client synchronization.

### Acceptance criteria

A player can create, persist, reopen, and reset valid layouts at supported desktop sizes; modules cannot be resized into broken states; and layout migration or corrupt preferences fall back safely.

## 16. Mobile / Tablet App

**Status:** Planned

### Outcome

The established companion experience becomes usable on supported phones and tablets without treating a small screen as a shrunken desktop.

### Scope and behavior

- Adapt navigation, dashboard modules, density, input targets, and orientation behavior for touch devices.
- Define the first supported mobile platforms and minimum versions.
- Preserve connection, recovery, backgrounding, and resume behavior within platform constraints.
- Provide mobile-appropriate layout defaults while reusing compatible feature and theme boundaries.
- Document installation and local-network requirements before enabling device-to-PC connectivity.

### Dependencies and boundaries

This phase depends on the security work required for non-loopback connections. It does not imply internet access, a hosted relay, accounts, or identical layouts across desktop and mobile.

### Acceptance criteria

The supported mobile client can connect through the approved secure local flow, survive normal background and resume transitions, present existing features accessibly at target sizes, and explain network or pairing failures clearly.

## 17. Multi-Client Support

**Status:** Planned

### Outcome

More than one approved client can observe the same Skyrim session without corrupting state or making one screen silently authoritative over another.

### Scope and behavior

- Define session identity, capability negotiation, snapshot recovery, and update fan-out for concurrent clients.
- Bound client count, queue growth, backpressure, and slow-consumer behavior.
- Keep each client's local navigation and layout independent by default.
- Define synchronized-layout behavior separately and make participation explicit.
- Expose enough connection state to diagnose why a client was rejected or resynchronized.

### Dependencies and boundaries

This phase follows proven desktop and mobile clients. It does not add accounts, cloud presence, collaborative gameplay, or remote control permissions.

### Acceptance criteria

The supported number of clients receives consistent authoritative state, a slow or reconnecting client cannot destabilize Skyrim or other clients, and synchronization behavior is covered by integration scenarios.

## 18. Item Knowledge and Search

**Status:** Planned

### Outcome

Players can search a reference source for items and understand where or how they may be acquired without mixing static knowledge with live inventory truth.

### Scope and behavior

- Provide a searchable, versioned knowledge source with approved names, categories, descriptions, acquisition guidance, and source context.
- Keep large reference data outside the live bridge stream.
- Link knowledge results to live inventory or map state only through stable identities and explicit confidence.
- Support base-game content first and make data provenance and game-version compatibility visible.
- Define update, cache, localization, attribution, and unavailable-data behavior.

### Dependencies and boundaries

The knowledge source is advisory and must not claim that an item currently exists at a location when Skyrim state cannot confirm it. LOTD collection state and broad mod databases remain separate.

### Acceptance criteria

Search is responsive, results identify their source and supported game version, live-state links cannot create false ownership or location claims, and outdated or unavailable knowledge packages fail visibly.

## 19. Legacy of the Dragonborn Integration

**Status:** Planned

### Outcome

Players using a supported Legacy of the Dragonborn setup can relate item knowledge to collection and display progress.

### Scope and behavior

- Detect explicitly supported LOTD versions and required companion data.
- Add acquisition guidance, source quests or locations, supported-mod context, and collected or displayed state where those values can be determined reliably.
- Keep save-aware live state distinct from static museum and item knowledge.
- Explain unsupported versions, missing patches, ambiguous replicas, and incomplete state.
- Version compatibility data independently from the core client where practical.

### Dependencies and boundaries

LOTD support is optional and must not be required for inventory, item knowledge, or the map. DovahLink does not alter museum state, repair displays, or promise support for every LOTD patch.

### Acceptance criteria

Supported setups report verified collection state accurately across representative saves, unsupported setups fall back to ordinary item knowledge, and no ambiguous value is presented as authoritative.

## 20. Installed UI Detection

**Status:** Planned

### Outcome

DovahLink can identify selected installed Skyrim UI and font resources that may improve visual adaptation while preserving its complete native theme.

### Scope and behavior

- Define the small set of UI installations, font resources, and versions that can be detected reliably.
- Report detected capabilities through a stable boundary rather than exposing arbitrary file paths to presentation widgets.
- Handle conflicts, partial installations, overrides, and unsupported versions explicitly.
- Cache detection results only with a clear invalidation strategy.
- Allow the player to disable automatic adaptation and return to the native DovahLink theme.

### Dependencies and boundaries

Detection follows the Core UI Theme System and Customizable Dashboard. It must not modify Skyrim files, require a specific mod manager, infer compatibility from a name alone, or change application behavior beyond approved presentation values.

### Acceptance criteria

Supported installations are identified reproducibly, ambiguous or unsupported setups do not activate an adapter, disabling detection restores the native theme, and detection failure cannot prevent the client from starting.

## 21. Optional UI Mod Adapters

**Status:** Planned

### Outcome

Players may make DovahLink visually complement an explicitly supported Skyrim UI setup without coupling the core client to that mod.

### Scope and behavior

- Add opt-in adapters for approved versions of themes such as SkyUI, Nordic UI, Dear Diary, and other explicitly supported UI mods.
- Map supported fonts, colors, panels, icons, spacing, and animations into the existing DovahLink theme boundary.
- Define which values each adapter owns and how unsupported values fall back to the native theme.
- Validate compatibility independently for each adapter and supported mod version.
- Evaluate a versioned, constrained `dovahlink-theme.json` format for compatible theme packs, including validation, safe defaults, and error reporting before implementation approval.

### Dependencies and boundaries

Adapters cannot replace dashboard structure, feature behavior, protocol models, executable code, or arbitrary application resources. A custom theme format must remain declarative and cannot become a general plugin system.

### Acceptance criteria

Each approved adapter passes visual, fallback, accessibility, and unsupported-version checks; removing or breaking the source mod returns DovahLink to a usable native theme; and invalid custom theme data is rejected with a clear explanation.

## 22. Advanced Bridge Improvements

**Status:** Planned

### Outcome

The bridge can be hardened or extended in response to measured needs from the completed product features without turning into an unbounded general Skyrim API.

### Scope and behavior

- Profile actual lifecycle, serialization, update-frequency, memory, and game-thread costs.
- Improve snapshot efficiency, capability negotiation, diagnostics, compatibility handling, and recovery where evidence shows a need.
- Consolidate repeated native integrations only after multiple implemented features demonstrate the same stable boundary.
- Expand supported runtime versions deliberately with reproducible compatibility tests.
- Document migration and fallback behavior for any protocol-affecting improvement.

### Dependencies and boundaries

This phase is evidence-driven and comes after feature usage reveals real constraints. It does not authorize arbitrary game mutation, broad scripting exposure, premature plugin APIs, remote connectivity, or permanent protocol complexity for hypothetical consumers.

### Acceptance criteria

Every improvement names the measured problem it solves, preserves or deliberately versions existing contracts, passes lifecycle and integration tests, and does not expand bridge authority beyond an approved product need.

## Deferred possibilities

The following ideas are deliberately not assigned a phase. They require a demonstrated player need and separate product, safety, and architecture decisions before entering the ordered roadmap:

- Safe actions from companion devices
- Arbitrary map-selected destinations
- A general plugin or extension platform
- Internet or hosted remote connectivity
- Accounts, cloud storage, or cross-device cloud synchronization
- Broad compatibility promises for unsupported mods, UI themes, or Skyrim editions

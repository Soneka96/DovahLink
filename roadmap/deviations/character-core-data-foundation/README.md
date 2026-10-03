# Character Core Data Foundation

**Status:** Active

This deviation adds the production character data needed by the approved DovahLink prototype
before the currently ordered client presentation work. It leaves the main roadmap's stage numbering
and completed stages intact. The work is a backend and data-contract foundation; it does not add
Flutter UI.

## Scope and ownership

The intended path remains:

```text
Skyrim
    ↓
specific Adapter capture leaf
    ↓
existing generic Adapter capture pipeline
    ↓
Host state-area registration and publication
    ↓
typed protocol
    ↓
Dart SDK synchronization-aware state streams
```

The Adapter's generic request dispatch, IPC architecture, scheduling framework, queue semantics,
transport, backpressure, Host/Adapter routing, and generic response handling are unchanged. Native
work is limited to specific capture leaves, including extending the existing coherent Vitals read.
The fixed `CapturedPayload` capacity grows from 12 to 24 bytes because the coherent Vitals result
now contains six float32 values; splitting those values into separate captures would lose the
requested observation coherence.
The Host continues to register and publish independently authoritative state areas through its
existing machinery. Current and effective maximum Health, Magicka, and Stamina form one coherent
`character_vitals` domain. `character_xp` and `character_level` remain independent domains; the
retired aggregate `character` area stays retired, and existing `character_level` behavior is
preserved.

No Flutter screens, location, game time, tracked quests, or gameplay-session/main-menu lifecycle
are part of this deviation.

## Contract decisions

Every new state area is independently subscribable and uses the existing `value` envelope shape.
Unavailable data is `value: null`; the Adapter and Host do not substitute plausible values. A
malformed private capture remains a rejected update, not a public unavailable value. Snapshot areas
advance revisions only under the existing Host change/recovery rules.

| State area | Meaning | Skyrim source / transformation rule | Cadence | Delivery | Unavailable behavior |
| --- | --- | --- | --- | --- | --- |
| `character_vitals` | Current and effective maximum Health, Magicka, and Stamina; each resource contains numeric `current` and `max` values | One coherent read from the same player observation. Current values use `ActorValueOwner::GetActorValue`; each maximum uses CommonLibSSE-NG 9.0.0 `Actor::GetActorValueMax`. Preserve raw single-precision values without UI clamping. | Fast, one coherent capture | Snapshot | `value: null` for the whole domain when the player or any required actor value is unavailable |
| `character_identity` (future candidate; not implemented) | Candidate grouping for player name, identity race, and supernatural traits, if they share one authoritative capture and lifecycle | Name comes from the player identity record, not device metadata or a synthesized label. Identity race must not be inferred from the current runtime race. Traits mean independent character statuses/capabilities; they may combine and must not imply one another. Confirm every source and predicate from supported Skyrim/API evidence before publishing. | To be decided from verified source cadence and shared lifecycle; no Slow `RateClass` currently exists | Snapshot candidate only if shared semantics are established | To be defined from the authoritative capture; never synthesize plausible defaults |

The public value shapes are:

```json
{"value": {"health": {"current": 327.0, "max": 410.0}, "magicka": {"current": 180.0, "max": 250.0}, "stamina": {"current": 120.0, "max": 190.0}}}
{"value": "localized or modded display name"}
{"value": {"isVampire": true, "hasVampireLordForm": true, "isWerewolf": false}}
```

The first object is the current `character_vitals` value. The following values illustrate possible
identity members only; `character_identity` is not implemented or part of the current protocol
schema. Its eventual grouping and value shape depend on verified shared capture and availability
semantics. An available supernatural-traits value requires all three booleans; do not substitute a
single enum or a partially available object.

Vitals values are sent as Skyrim reports them: no percentage conversion, `0..100` clamp, or
current-versus-maximum normalization. The pinned CommonLibSSE-NG 9.0.0 port is commit
`5decf47b01dde5501b03afaa91cd4d182e793cca` in `tooling/vcpkg-ports/commonlibsse-ng-flatrim/`.
Its `RE::Actor::GetActorValueMax` implementation is in `src/RE/A/Actor.cpp` and returns
`GetPermanentActorValue(a_value) + GetActorValueModifier(ACTOR_VALUE_MODIFIER::kTemporary,
a_value)`. The public meaning does not promise behavior beyond those engine channels. Existing
current values continue to use `ActorValueOwner::GetActorValue` and keep their current public
contracts.

Name and race are sampled so later player identity changes, including supported RaceMenu changes,
can be observed without adding event plumbing. A missing or unusable identity during menu, New
Game, character creation, or save transitions is unavailable. Capture availability does not decide
Host/session admission; playable-context lifecycle remains a later phase.

The prototype's singular `Faction` concept is intentionally replaced by
`character_supernatural_traits`. Faction is not an adequate model for these independent character
conditions: vampirism, possession of Vampire Lord form, and lycanthropy are not guaranteed mutually
exclusive. Vampire Lord capability does not mean currently transformed, and lycanthropy does not
mean currently in Beast Form. No English display-string detection or forced implications between
fields are allowed. Known vanilla/Dawnguard-specific predicate limits and mod compatibility must be
recorded when authoritative predicates are selected.

## Source-verification gates

The effective maximum source is established by the pinned CommonLib headers/source and the native
implementation of `Actor::GetActorValueMax`: `GetPermanentActorValue(a_value) +
GetActorValueModifier(kTemporary, a_value)`. The implementation must use this API in the existing
coherent Vitals leaf and must not turn six values into separate scheduled reads.

Identity race and supernatural trait implementation remain gated on confirming semantics against
the actual supported CommonLibSSE-NG / Skyrim APIs. In particular, the current actor race is not
identity race. If a robust identity-race source or any of the three independent trait predicates
cannot be established, omit that portion and report the ambiguity; do not ship guessed semantics.

## Current and future public areas

The current protocol registers exactly `character_vitals`, `character_xp`, and `character_level`.
The Vitals group shares one revision because all six values come from the same Fast capture and
observation instant. XP remains independently authoritative, and Level retains its Event updates
with an authoritative Snapshot baseline and recovery.

Character name, identity race, and supernatural traits remain deferred. `character_identity` is a
candidate domain only if repository and Skyrim-source investigation confirms they share capture
source, observation coherence, cadence, authority, availability, revision, and recovery semantics.
Otherwise split only the independently authoritative domains established by that evidence. Do not
add a giant `character` state value or separate scalar areas for fields from one coherent capture.

Each new area follows the same authority continuity, `playContextId`, revision, Snapshot recovery,
subscription, stale-state, and invalidation semantics as existing state. The SDK exposes typed
values through the existing synchronization-aware API; supernatural traits are an immutable value,
not a raw map.

## Follow-on order

After this foundation, the separately scoped phases are:

1. **World Context Data Foundation:** location, game time, and tracked quest summary.
2. **Active Play Context Lifecycle:** main menu, New Game, loading, save switching, return to menu,
   and gameplay-session admission/closure.
3. **Session Overview convergence:** connect the approved prototype Overview to the trustworthy
   backend/SDK data after the foundations above.

These are follow-on phases, not implementation scope for this deviation. Normal roadmap progression
resumes only after this deviation and its explicitly ordered follow-on work are complete or
re-planned by the maintainer.

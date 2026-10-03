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
existing machinery. The retired aggregate `character` state area stays retired, and existing
`character_level` behavior is preserved.

No Flutter screens, location, game time, tracked quests, or gameplay-session/main-menu lifecycle
are part of this deviation.

## Contract decisions

Every new state area is independently subscribable and uses the existing `value` envelope shape.
Unavailable data is `value: null`; the Adapter and Host do not substitute plausible values. A
malformed private capture remains a rejected update, not a public unavailable value. Snapshot areas
advance revisions only under the existing Host change/recovery rules.

| State area | Meaning | Skyrim source / transformation rule | Cadence | Delivery | Unavailable behavior |
| --- | --- | --- | --- | --- | --- |
| `character_health_max` | Current effective maximum Health actor value, in Skyrim's raw single-precision units | The same player read as current Vitals; use CommonLibSSE-NG 9.0.0 `Actor::GetActorValueMax(kHealth)`. Its pinned implementation returns permanent actor value plus the temporary actor-value modifier. Keep the raw result, including values outside UI expectations. | Fast, same coherent capture as current Health/Magicka/Stamina | Snapshot | `value: null` when player or actor-value access is unavailable |
| `character_magicka_max` | Current effective maximum Magicka actor value, in raw units | Same coherent player read; `Actor::GetActorValueMax(kMagicka)` | Fast, same coherent Vitals capture | Snapshot | `value: null` when player or actor-value access is unavailable |
| `character_stamina_max` | Current effective maximum Stamina actor value, in raw units | Same coherent player read; `Actor::GetActorValueMax(kStamina)` | Fast, same coherent Vitals capture | Snapshot | `value: null` when player or actor-value access is unavailable |
| `character_name` | Actual player character display name; JSON string in `value` | Read from the underlying player `TESNPC` identity record's full name, not Windows/device metadata or a synthesized label. A missing identity, null name, or empty name remains unavailable. | Requested slow behavior at approximately 1 Hz, using the existing Medium interval because the Host currently has no Slow `RateClass` | Snapshot | `value: null` when player identity/name is unavailable |
| `character_race` | Localized race display name from the running game; JSON string in `value` | Candidate source is the identity/base `TESNPC` race's localized full name, not the current runtime race. CommonLibSSE-NG 9.0.0 `Actor::GetRace()` returns the actor's runtime race when set before falling back to base race, so it is not sufficient evidence. Confirm the base-record source across transformations and legitimate RaceMenu changes before publishing. | Requested slow behavior at approximately 1 Hz, using existing Medium interval; no Slow `RateClass` currently exists | Snapshot | `value: null` when authoritative player identity/race or usable display name is unavailable |
| `character_supernatural_traits` | An object in `value` with required boolean fields `isVampire`, `hasVampireLordForm`, and `isWerewolf` | Traits mean character status/capability, not current transformation. They may combine; do not normalize one field from another. The exact authoritative Skyrim predicates for each field are an implementation gate and must be documented from source/API evidence before capture is added. | Requested slow behavior at approximately 1 Hz, using existing Medium interval; no Slow `RateClass` currently exists | Snapshot | `value: null` when authoritative player state is unavailable; available all-false is distinct |

The public value shapes are:

```json
{"value": 410.0}
{"value": "localized or modded display name"}
{"value": {"isVampire": true, "hasVampireLordForm": true, "isWerewolf": false}}
```

Maximum, name, and race areas use a JSON number or string respectively. The supernatural object
requires all three booleans when available; it is never a single enum or a partially available
object. Examples are value-shape illustrations only; the canonical public schema is updated when
the corresponding implementation is added.

Maximum values are sent as Skyrim reports them: no percentage conversion, `0..100` clamp, or
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

## Planned public areas

Existing registered areas stay unchanged: `character_xp`, `character_health`, `character_magicka`,
`character_stamina`, and `character_level`. Additive areas are `character_health_max`,
`character_magicka_max`, `character_stamina_max`, `character_name`, `character_race`, and
`character_supernatural_traits`. No aggregate `character` state area or duplicate level stream is
introduced.

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

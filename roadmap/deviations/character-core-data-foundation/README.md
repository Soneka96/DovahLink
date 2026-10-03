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
| `character_identity` | Player display name and identity race from one complete identity observation | Display name uses `PlayerCharacter::GetDisplayFullName()`. Identity race uses `PlayerCharacter::GetRaceData().charGenRace`, then the race's game-provided full display name. `Actor::GetRace()`, ActorBase race, and `RaceData.race2` are rejected. | Slow, one complete capture | Snapshot | `value: null` when the player, usable display name, `charGenRace`, or usable race display name is unavailable; never publish a partial object |
| `character_supernatural_traits` | Independent vampire status and Vampire Lord / Werewolf transformation capabilities | `isVampire` uses the `kPlayerIsVampireVariable` `TESGlobal`; `hasVampireLordForm` uses possession of Dawnguard's `DLC1VampireChange`; `hasWerewolfForm` uses possession of the canonical Beast Form spell. `PlayerIsWerewolf` is not the public predicate. | Slow, one complete capture | Snapshot | `value: null` if the player or any required global/form lookup is unavailable; never publish partial or fabricated booleans |

The public value shapes are:

```json
{"value": {"health": {"current": 327.0, "max": 410.0}, "magicka": {"current": 180.0, "max": 250.0}, "stamina": {"current": 120.0, "max": 190.0}}}
{"value": {"name": "Goncalo", "race": "Nord"}}
{"value": {"isVampire": true, "hasVampireLordForm": true, "hasWerewolfForm": false}}
```

These are the `character_vitals`, `character_identity`, and
`character_supernatural_traits` values. For either new domain, unavailable is `{"value": null}`.
Identity is atomic: do not publish a name without its race or a race without its name. An available
supernatural-traits value contains all three booleans; all-false is a valid synchronized value and
is distinct from unavailable.

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
conditions: vampirism, possession of Vampire Lord form, and possession of Beast Form are not
guaranteed mutually exclusive. Vampire Lord capability does not mean currently transformed, and
Beast Form capability does not mean currently transformed. No English display-string detection or
forced implications between fields are allowed. These domains report the selected Skyrim sources;
they do not normalize unusual mod, bug, or console-created combinations.

## Approved sources and runtime evidence

The effective maximum source is established by the pinned CommonLib headers/source and the native
implementation of `Actor::GetActorValueMax`: `GetPermanentActorValue(a_value) +
GetActorValueModifier(kTemporary, a_value)`. The implementation must use this API in the existing
coherent Vitals leaf and must not turn six values into separate scheduled reads.

Runtime verification and pinned CommonLibSSE-NG inspection are sufficient to approve the exact
production sources and public semantics below. The implementation gate is open for the two
independent domains `character_identity` and `character_supernatural_traits`; the remaining limits
are compatibility notes, not blockers for this approved scope.

### Character Identity source verification

The static CommonLib cross-check used the repository-pinned revision
`5decf47b01dde5501b03afaa91cd4d182e793cca` from
`tooling/vcpkg-ports/commonlibsse-ng-flatrim/`. Links below are pinned to that revision so later
CommonLib changes do not silently rewrite what this investigation established.

| Semantic | Evidence | Runtime observation | Approved production source / rejected source | Known limit |
| --- | --- | --- | --- | --- |
| Player display name | `GetDisplayFullName()` is the player display-name API; the observed `ç` and `á` bytes were valid UTF-8. `TESFullName::fullName` uses `BSFixedString` character storage, which does not itself promise UTF-8. [Pinned `TESObjectREFR.cpp`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/src/RE/T/TESObjectREFR.cpp), [pinned `TESFullName.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/T/TESFullName.h), [pinned `BSFixedString.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/B/BSFixedString.h). | The accepted RaceMenu name remained correct through Werewolf and Vampire Lord transformations and after save/load. The tested `ç` and `á` bytes were `C3 A7` and `C3 A1`. | Use `PlayerCharacter::GetDisplayFullName()`. Reject missing, empty, oversized, or invalid UTF-8 names as an unavailable whole `character_identity`; do not use device metadata or synthesize a fallback. | The non-ASCII runtime sample covered the player name, not a modded/localized race name. Validate both strings strictly at the capture boundary. |
| Identity race | Pinned `Actor::GetRace()` prefers runtime race when available and otherwise uses ActorBase race. `PlayerCharacter::RaceData` exposes `charGenRace` and `race2`. [Pinned `Actor.cpp`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/src/RE/A/Actor.cpp), [pinned `PlayerCharacter.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/P/PlayerCharacter.h). | After a RaceMenu change from High Elf to Nord, `charGenRace` became Nord. It stayed Nord during Werewolf and Vampire Lord forms and after reversion/save/load. `race2` remained High Elf during the live post-RaceMenu session and was stale. | Use `PlayerCharacter::GetRaceData().charGenRace`. Resolve its actual game display/full name through the pinned race `TESFullName` API; localization and modded display text are authoritative. Reject `Actor::GetRace()`, ActorBase current race, `race2`, English FormID maps, and partial identity values. | Tested vanilla/Dawnguard setup; other RaceMenu replacements, race overhauls, and transformation implementations were not surveyed. |
| Vampire status | The pinned default-object manager provides `kPlayerIsVampireVariable`, resolved as `TESGlobal`. [Pinned default-object definitions](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/B/BGSDefaultObjectManager.h). | The global was nonzero in humanoid vampire and Vampire Lord forms, became zero immediately after the observed cure, and remained independent from the werewolf signals in the tested hybrid setup. | Resolve `BGSDefaultObjectManager::DefaultObjectID::kPlayerIsVampireVariable` as `TESGlobal`; `isVampire` is `global value != 0`. Missing global means the whole supernatural domain is unavailable. Do not infer from race, display string, Vampire Lord spell possession, or visual form. | Cure persistence was not conclusive in the later quick-save/load log; overhaul behavior was not surveyed. |
| Werewolf capability | The pinned CommonLib default-object definitions provide `kWerewolfSpell` and the typed `GetObject<T>` lookup. | The selected default object resolved as Beast Form (`0x00092C48`); observed possession tracks access to the transformation capability rather than current transformed race. | Use `BGSDefaultObjectManager::DefaultObjectID::kWerewolfSpell`, resolved as `SpellItem`, and `player.HasSpell(...)` for `hasWerewolfForm`. Do not use `PlayerIsWerewolf`: it remained true in an observed state without Beast Form and can serve as a compatibility signal. Missing spell lookup means the whole supernatural domain is unavailable. | A successful vanilla cure was not observed. The public field intentionally means capability possession, not a cure-aware species/status predicate. Mod and overhaul changes can alter spell ownership. |
| Vampire Lord capability | Dawnguard's `DLC1VampireChange` is the transformation spell. The pinned API provides plugin-qualified `TESDataHandler::LookupForm`. [Pinned `TESDataHandler.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/T/TESDataHandler.h). | The spell was present in Vampire Lord-capable humanoid state, during transformation, after reversion/save-load, and absent after the observed cure. | Resolve local form `0x0283B` from `Dawnguard.esm` and use player spell possession for `hasVampireLordForm`. Never hardcode observed runtime ID `0x0200283B`. A failed lookup means the whole supernatural domain is unavailable. | Natural quest acquisition was not tested; overhauls may change spell/cure behavior. |

The observations came from the temporary `IDENTITY_DIAG` game-thread diagnostic in
`adapter/runtime/commonlib_adapter_character_capture.cpp`. That diagnostic hook was removed after
the session and the ordinary Release Adapter was rebuilt; no diagnostic source change remains.
The observations establish the tested setup only, not a guarantee for every Skyrim runtime or mod
list. No automated mock test is evidence for these engine semantics.

The supernatural fields must remain independent. A console-assisted state was observed with both
`PlayerIsVampire=1` and `PlayerIsWerewolf=1`, with both Vampire Lord and Beast Form spells present;
the subsequent cure experiment also showed that one status may change while another persists.
Preserve such combinations if capture observes them. Do not impose mutual exclusion or infer
Vampire Lord capability from vampirism, or werewolf status from current beast race.

For future native-to-private-payload copying, a RaceMenu name containing `ç` and `á` produced
valid UTF-8 bytes and survived the observed name changes and save/load. This is sample-level runtime
evidence, not a universal encoding guarantee for every game/mod string. Validate UTF-8 strictly at
the native boundary and copy the text into owned storage before crossing the capture boundary; do
not retain a borrowed engine string beyond its valid lifetime. No non-ASCII race display string was
tested.

The tested runtime does not establish behavior for every Skyrim overhaul or mod list. A successful
vanilla Werewolf cure and natural Vampire Lord acquisition were not observed, and the later vampire
quick-save/load observation did not establish whether the cure persisted. These limits do not
change the approved capability predicates. Capture still treats absent player/source data and
character-creation/menu states with unusable identity as unavailable; it does not infer menu state
from a name string. No reliable active character-creation flag was identified in the inspected
`PlayerCharacter::ByCharGenFlag` declarations.

## Current and future public areas

The current protocol registers `character_vitals`, `character_xp`, and `character_level`.
The Vitals group shares one revision because all six values come from the same Fast capture and
observation instant. XP remains independently authoritative, and Level retains its Event updates
with an authoritative Snapshot baseline and recovery.

The approved production additions are two independently authoritative domains:
`character_identity` groups name and identity race into one complete identity observation, while
`character_supernatural_traits` groups the three independently reported capability/status values.
Do not combine them into one identity object, one giant `character` state value, or five scalar
areas. `character_identity` is unavailable unless both members are usable. The supernatural domain
is unavailable unless all three source values are established; its booleans are otherwise
independent and all eight Boolean combinations are valid data shapes.

Each new area follows the same authority continuity, `playContextId`, revision, Snapshot recovery,
subscription, stale-state, and invalidation semantics as existing state. The SDK exposes typed
values through the existing synchronization-aware API; supernatural traits are an immutable value,
not a raw map.

### Future Flutter Overview presentation note

The backend retains `isVampire`, `hasVampireLordForm`, and `hasWerewolfForm`. The later Flutter
Overview derives display metadata: show “Vampire Lord” when `hasVampireLordForm` is true, otherwise
show “Vampire” when `isVampire` is true; independently include “Werewolf” when `hasWerewolfForm` is
true. When no supported supernatural trait is active, omit the supernatural segment entirely; do
not display “Mortal” or “None” or leave a dangling separator. Examples include `Nord · Level 43`,
`Nord · Level 43 · Vampire`, `Nord · Level 43 · Vampire Lord`, `Nord · Level 43 · Werewolf`,
`Nord · Level 43 · Vampire, Werewolf`, and `Nord · Level 43 · Vampire Lord, Werewolf`. This is a
presentation contract only; no Flutter code belongs to this deviation.

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

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
| `character_identity` (future candidate; not implemented) | Candidate grouping for player name, identity race, and supernatural traits, if they share one authoritative capture and lifecycle | Runtime evidence supports `GetDisplayFullName()` for the player display name, `PlayerCharacter::GetRaceData().charGenRace` as the current identity-race capture candidate, the `PlayerIsVampire` global for vampirism, possession of Dawnguard's `DLC1VampireChange` for Vampire Lord capability, and Beast Form possession as a werewolf proxy. See "Character Identity source verification" below for limits. These are candidate sources, not production registration or a public contract. Traits are independent and may combine; never infer one from another. | To be decided from verified source cadence and shared lifecycle; no Slow `RateClass` currently exists | Snapshot candidate only if shared semantics are established | To be defined from the authoritative capture; never synthesize plausible defaults |

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

Identity sources have now been exercised in a live Skyrim session and cross-checked against the
pinned CommonLibSSE-NG revision and available game-script evidence. The evidence and its remaining
limits are recorded below. This closes part of the source-discovery gate; it does not implement or
register `character_identity`, establish its shared capture cadence/lifecycle, or approve a public
contract.

### Character Identity source verification

The static CommonLib cross-check used the repository-pinned revision
`5decf47b01dde5501b03afaa91cd4d182e793cca` from
`tooling/vcpkg-ports/commonlibsse-ng-flatrim/`. Links below are pinned to that revision so later
CommonLib changes do not silently rewrite what this investigation established.

| Semantic | Candidates and evidence | Runtime observation | Current capture candidate / rejected source | Remaining limit |
| --- | --- | --- | --- | --- |
| Player display name | `TESObjectREFR::GetName()` resolves through the referenced object's name; `GetDisplayFullName()` is a separate player/reference API; the ActorBase `TESFullName` is base-record data. `TESFullName::fullName` is a `BSFixedString` (`char` storage), but these declarations do not themselves document an encoding or prove RaceMenu behavior. [Pinned `TESObjectREFR.cpp`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/src/RE/T/TESObjectREFR.cpp), [pinned `TESFullName.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/T/TESFullName.h), [pinned `BSFixedString.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/B/BSFixedString.h). | `GetName()`, `GetDisplayFullName()`, ActorBase `GetName()`, and `TESNPC::fullName` all showed the accepted RaceMenu name, including after transformations and save/load. The log bytes for the tested `ç` and `á` characters were `C3 A7` and `C3 A1`, consistent with UTF-8. | `GetDisplayFullName()` is the best semantic candidate for the player's displayed full name. All four candidates agreed in observed states, so runtime evidence did not distinguish their mutation/ownership behavior. Do not use device metadata or synthesize a name. | No different candidate values were observed; a modded/localized non-ASCII race name was not tested. `BSFixedString`'s `char` representation alone does not guarantee UTF-8 for every source. |
| Current identity race | Pinned `Actor::GetRace()` prefers the actor's runtime race when available and otherwise uses ActorBase race; it therefore reports transformed body race in relevant states. `PlayerCharacter::RaceData` has `charGenRace` and `race2`, but the field layout alone does not define their runtime semantics. [Pinned `Actor.cpp`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/src/RE/A/Actor.cpp), [pinned `PlayerCharacter.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/P/PlayerCharacter.h). | After RaceMenu changed High Elf (`0x00013743`) to Nord (`0x00013746`), `charGenRace` became Nord. In Werewolf (`0x000CDD84`) and Vampire Lord (`0x0200283A`) forms, runtime race and ActorBase race became the beast race while `charGenRace` remained Nord. It remained Nord after reverting and save/load. In the live post-RaceMenu session, `race2` stayed High Elf; after save/load it became Nord. | `PlayerCharacter::GetRaceData().charGenRace` is the current capture candidate for identity race: it followed a legitimate RaceMenu change and stayed stable through both transformations. Reject `Actor::GetRace()` and ActorBase race because they became the transformed body race; reject `race2` because it was stale during the live session. | Observed on the tested vanilla/Dawnguard setup. Other RaceMenu replacements, race overhauls, and mod-specific transformation implementations were not surveyed. |
| Vampire status | Pinned `BGSDefaultObjectManager::DefaultObjectID` provides `kPlayerIsVampireVariable`; the manager can resolve it as a `TESGlobal`. Available Dawnguard script copies describe the vampire quest setting/clearing `PlayerIsVampire` and removing `DLC1VampireChange` on cure, but one published script dump is secondary/old and is not treated as sole authority. Mod author discussion also describes vampire keyword detection as an alternative, not stronger evidence than the runtime global observation. [Pinned default-object definitions](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/B/BGSDefaultObjectManager.h), [published Dawnguard script copy](https://www.gamesas.com/dawnguard-psc-files-t289546-150.html), [Growl author discussion](https://www.reddit.com/r/EnaiRim/comments/1hihdro/detecting-that-player-is-vampirewerewolf/). | `PlayerIsVampire` (`0x000ED06D`) was `1` in humanoid states with runtime race both Nord (`0x00013746`) and NordRaceVampire (`0x00088794`), and during Vampire Lord form; it was `0` immediately after the observed vampire cure. It remained independent of werewolf status in the console-assisted hybrid state. A later quick-save/load log showed the vampire global back at `1` and Vampire Lord spell present; it is unclear whether that load restored a pre-cure save or the cure state failed to persist. | Candidate: resolve `kPlayerIsVampireVariable` as `TESGlobal` and interpret nonzero as true. Current race is not a suitable primary predicate because it represents the body and changes during forms. | The vampire state was console-assisted and the hybrid was not established as ordinary vanilla progression. Cure persistence across save/load is unresolved; overhaul behavior was not surveyed. |
| Werewolf status | The pinned default-object manager exposes `kPlayerIsWerewolfVariable` as a `TESGlobal`; the declaration establishes the object/type, not complete cure or transformation semantics. Mod author and script evidence show this global is also maintained as a compatibility signal: Skyrim Unbound Reborn sets it when granting lycanthropy; Growl describes Beast Form or `C00.PlayerHasBeastBlood` as its werewolf detection path. The latter variable was not observed in this adapter diagnostic. [Pinned default-object definitions](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/B/BGSDefaultObjectManager.h), [Skyrim Unbound issue](https://www.nexusmods.com/skyrimspecialedition/mods/27962?issue_id=483368&tab=logs), [Growl author discussion](https://www.reddit.com/r/EnaiRim/comments/1hihdro/detecting-that-player-is-vampirewerewolf/). | `PlayerIsWerewolf` (`0x000ED06C`) became `1` after the in-game lycanthropy sequence and remained `1` in humanoid and Werewolf form. In a later quick-save/load log it was still `1` while Beast Form was absent. The earlier attempted cure used a forced/stuck Companions quest-stage path with Kodlak's ghost missing, so it was not a verified completion of the vanilla cure ritual. | Do not use the global alone as the werewolf predicate: the observed true value after Beast Form was absent conflicts with a clean cure-aware status predicate, and mods may explicitly set the global for compatibility. Candidate: possession of `kWerewolfSpell` (resolved at runtime as Beast Form, `0x00092C48`) for access to lycanthropy. This remains a capability/condition proxy, not proof of current beast form. | A successful vanilla cure was not observed. The compatibility-global evidence comes from mod-author/script sources; modded implementations may differ. Re-test the global and spell across a clean cure before finalizing the public field's exact meaning. |
| Vampire Lord capability | Dawnguard's `DLC1VampireChange` is the transformation spell candidate; script evidence describes its removal on cure. Resolve it by plugin/local form ID, not a load-order-dependent runtime ID. The pinned API provides `TESDataHandler::LookupForm` for plugin-qualified lookup. [Pinned `TESDataHandler.h`](https://github.com/alandtse/CommonLibSSE-NG/blob/5decf47b01dde5501b03afaa91cd4d182e793cca/include/RE/T/TESDataHandler.h). | The spell candidate was present in humanoid Vampire Lord-capable state, during transformation, after reversion, and after save/load; it was absent after the observed vampire cure. The transformed race itself was `DLC1VampireBeastRace`. | Candidate: `player.HasSpell(DLC1VampireChange)` means access to the form/capability, not currently transformed. Resolve local ID `0x0283B` from `Dawnguard.esm` using `LookupForm`; the observed runtime ID was `0x0200283B`, which must not be hardcoded. | Spell was granted by console for this test; natural quest acquisition was not tested. Overhauls can alter spell/cure behavior. |

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

The following remain open before implementation: verify werewolf status after a successfully
completed vanilla cure; test natural Vampire Lord acquisition; establish behavior against the
supported runtime/mod compatibility target; determine whether name and race plus all three traits
share capture cadence, observation coherence, availability, and lifecycle; and define how
character-creation/menu states are recognized. No reliable active character-creation flag was
identified in the inspected `PlayerCharacter::ByCharGenFlag` declarations. Do not infer that state
from a name string.

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

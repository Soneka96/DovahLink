# World Context Data Foundation Research

**Status:** Runtime research is complete. The frozen findings below now inform the production World Context implementation.

## Purpose and scope

This report preserves the runtime evidence collected for the World Context data described in the
[Character Core Data Foundation deviation](../character-core-data-foundation/README.md). It covers
location, game time, quest tracking, objectives, and quest targets from the tested Skyrim Special
Edition setup. It records observed source semantics, not compatibility with every mod list.

The runtime observations came from the Adapter's temporary Debug World Context diagnostics and the
maintainer's in-game Journal, map, console, and fast-travel checks. The diagnostic was intentionally
read-only. The one objective-failure test used the console command documented below and changed a
disposable test save.

## Findings

### Location and game time

- The sampled `playerLocation`, `cellLocation`, and `worldspace` values describe separate parts of
  the game state. Around Whiterun, the outdoor cell reported worldspace Skyrim while the city-gate
  cell reported worldspace Whiterun; both location candidates still identified Whiterun. The
  Bannered Mare and Bleak Falls Temple interior samples had named interior cells and no worldspace.
- The sampled location candidates agreed in the observed locations, including the Bannered Mare,
  Whiterun exterior, and Bleak Falls Temple. This is evidence for those samples, not a guarantee
  that both APIs always agree.
- Calendar backing globals for year, month, day, hour, and timescale were valid in the observed
  gameplay samples. In-game fast travel advanced the calendar time substantially while the
  timescale remained 20. COC cell changes and in-game fast travel were separately identified in
  the test notes; they must not be treated as the same transition.

### Quest tracking and objectives

- Quest tracking is plural. Papyrus documents `Quest.IsActive()` as whether that quest is currently
  tracked by the player, and the quest exposes `SetActive` for that state. Runtime samples showed
  multiple quests with `activeFlag=true` and `activeMethod=true` at once. Model tracking per quest;
  there is no single focused quest to infer.
- `PlayerCharacter` stores a quest-to-targets map and a separate array of instantiated objective
  records. The diagnostic reported target counts per quest and objective states, but did not map
  each target reference back to its specific objective. The pinned `BGSQuestObjective` definition
  does expose objective-specific target references and a target count.
- Objective state is independent of quest tracking. The pinned enum defines dormant (0), displayed
  (1), completed (2), completed-and-displayed (3), failed (4), and failed-and-displayed (5). The
  diagnostic's `displayed`, `completed`, `failed`, and `dormant` fields are derived from those
  states. Multiple objectives can be displayed at once; the maintainer observed their markers
  appearing together on the map.
- A failed-and-displayed state was verified in-game on the disposable test save. Running
  `SetObjectiveFailed 0001CEF4 30 1` changed The Silver Hand objective 30, “Kill the werewolf
  hunters,” to `state=5 displayed=true completed=false failed=true`. The Journal showed the red
  failure mark. The quest itself remained active/tracked, and its other objective states did not
  change. This validates capture of the engine state; it does not validate a quest's natural
  failure logic.
- The tested Unbound objectives “(Optional) Search the barrel for potions” and “(Optional) Attempt
  to pick the lock to the cage” appeared as dormant. The pinned `QUEST_OBJECTIVE_FLAGS` enum has no
  optional flag; dormant therefore must not be treated as synonymous with optional. Preserve the
  authored objective text and raw state rather than inventing an optional boolean from these
  observations.
- Miscellaneous is a quest type (`kMiscellaneous`) in the pinned game data. In the runtime tests,
  toggling the Journal's Miscellaneous heading produced no change in the captured quest sample,
  while toggling an individual entry did. This is consistent with the heading acting as a Journal
  grouping and the entries retaining their own quest/objective state. The category's visual
  expansion state was not exposed by the inspected capture.
- The quest log contains historical entries and instantiated completed objectives. Console-driven
  stage changes can create history that is not representative of natural progression. Keep current
  state fields distinct from journal history and do not infer tracking from historical entries.

## Handoff implications

The evidence supports a data model with multiple quest records, an independent tracked state for
each quest, and each quest's objective instances with their identity, authored text, ordering,
display/completion/failure state, and targets where those targets can be resolved reliably. It
does not support a singleton `selectedQuest` or `selectedObjective`, nor a generic optional boolean
derived from dormancy. The later Quests experience remains responsible for presentation and must
not fabricate hidden or unresolved marker information, consistent with [Stage 15 — Quests](../../15-quests.md).

This research does not settle the full target-resolution contract, objective ordering across every
mod, localization and malformed-data behavior, or main-menu/save-switch lifecycle. Those require
their own source and runtime validation during the corresponding implementation phases.

## Frozen implementation decisions

- `player_location` captures the current cell, `PlayerCharacter.currentLocation`,
  `TESObjectCELL.GetLocation()`, and the cell's worldspace as distinct bounded engine facts. The
  Host selects `currentLocation` when present and falls back to the cell location only when it is
  absent. A valid cell remains available when its location name or worldspace is absent. Runtime
  FormIDs identify these values only for the active loaded runtime. Optional names that are invalid
  UTF-8 or exceed the Adapter's 52-byte bound are omitted without truncating or invalidating the cell capture.
- `game_time` reads year, raw zero-based month, day, and fractional hour only from the Calendar's
  backing `TESGlobal`s after the player, Calendar, and all four float globals are available. The
  running game's localized month name comes from `Calendar.GetMonthName()`. The Host publishes
  month 1–12 and derives minutes by flooring the fractional hour; timescale and era are not state.
- `tracked_quests` includes only quests with `TESQuest.IsActive()` true. The Adapter returns
  `PlayerCharacter.objectives` instances only when the objective pointer is non-null and the
  referenced definition's `ownerQuest` is the exact tracked quest; it reads index and authored
  `displayText` from that definition, and state plus `instanceID` from the player-owned instance.
  Its bounded metadata page also returns `TESQuest.currentInstanceID`; the Host's frozen inclusion
  rule filters to matching objective instances, excluding prior quest-instance records without
  traversing quest-log history. The Adapter cursor advances over raw matching instances before
  this filter, so prior-instance records still consume pages and the Host's aggregate objective
  bound.
  The Adapter does not resolve instance-specific substitutions: available localized authored text is
  preserved as-is, and a missing display string is represented as null. The Adapter returns at most
  32 runtime quest IDs
  per page, one quest metadata item per response, and objective pages no larger than its existing
  255-byte capture bound. The Adapter owns no complete-collection limits. Titles and objective text
  are capped at 126 UTF-8 bytes. The Host assembles
  and sorts the complete result by runtime quest FormID, objective index, and instanceID. Repeated
  quest IDs in an otherwise valid collection collapse to one quest. The Host reads each unique
  quest's metadata before and after its objective pages; a title, type, or `currentInstanceID`
  change makes the capture unavailable. Identical current objective records with the same
  quest/index/instanceID are deduplicated, while conflicting records fail the capture. Failed pages,
  changed play context or tracked-ID set, more than 128 tracked quests, more than 1,024 raw objective
  records, or serialized state above 1 MiB make the area unavailable. The Host rechecks the
  tracked-ID set before publication; objective state can still advance while pages are read, so this
  remains a best-effort sample rather than an engine transaction. Runtime FormIDs are not durable
  identities and quest targets are excluded.

## Sources

- Repository-pinned CommonLibSSE-NG 9.0.0 (`5decf47b01dde5501b03afaa91cd4d182e793cca`):
  [`TESQuest.h`](../../../adapter/vcpkg_installed/x64-windows-dovahlink/include/RE/T/TESQuest.h),
  [`QuestObjectiveStates.h`](../../../adapter/vcpkg_installed/x64-windows-dovahlink/include/RE/Q/QuestObjectiveStates.h),
  [`BGSInstancedQuestObjective.h`](../../../adapter/vcpkg_installed/x64-windows-dovahlink/include/RE/B/BGSInstancedQuestObjective.h),
  and [`PlayerCharacter.h`](../../../adapter/vcpkg_installed/x64-windows-dovahlink/include/RE/P/PlayerCharacter.h).
- [Skyrim Papyrus Quest reference](https://papyrus.bellcube.dev/skyrimse/script/quest/) for `IsActive`,
  `SetActive`, and objective state functions.
- [SKSE console command reference](https://skse.silverlock.org/vanilla_commands.html) for
  `SetObjectiveFailed` and `GetObjectiveFailed`.

## Flutter state integration

PR #120 projects `player_location`, `game_time`, and the complete `tracked_quests` collection into
the app-owned live-state Redux boundary. The projection retains distinct cell, selected-location,
and worldspace facts; Skyrim calendar fields; and every tracked quest with its current objective
instances. It preserves SDK synchronization status so an available empty quest list remains distinct
from unavailable quest state. Route navigation does not own these subscriptions; the SDK owns
desired-intent restoration and reconnect recovery.

PR #122 delivered the state-backed Session Overview through the typed Session Overview ViewModel. It
does not add quest navigation or change the frozen capture contract above.

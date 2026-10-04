# World Context Data Foundation Research

**Status:** Research complete for the observed runtime and inspected Skyrim APIs. Production contract and implementation remain separate work.

## Purpose and scope

This report preserves the runtime evidence collected for the follow-on World Context Data
Foundation named in the [Character Core Data Foundation deviation](../character-core-data-foundation/README.md).
It covers location, game time, quest tracking, objectives, and quest targets from the tested Skyrim
Special Edition setup. It records observations and source semantics; it does not approve a public
protocol shape or claim compatibility with every mod list.

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

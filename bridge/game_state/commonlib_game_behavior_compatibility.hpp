#pragma once

namespace dovahlink::game_state {

/// Forces Skyrim's `bAlwaysActive:General` runtime setting on, so gameplay
/// continues while the DovahLink window has focus instead of pausing.
/// Logs and returns without effect if the setting cannot be resolved.
void ApplyAlwaysActiveSetting();

/// Installs a runtime patch that makes achievements eligible with SKSE
/// plugins loaded, following the technique documented in `bridge/README.md`.
/// Logs and returns without effect if the patch target's Address Library ID
/// is unmapped for the current runtime. If the ID is mapped but the
/// installed Address Library database is missing or incompatible, CommonLib
/// itself fails process startup before this function is reached; that
/// failure mode is inherent to every Address-Library-resolved symbol in this
/// plugin; see `bridge/README.md`'s supported-runtime section.
void InstallAchievementCompatibilityPatch();

}  // namespace dovahlink::game_state

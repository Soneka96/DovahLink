#pragma once

namespace dovahlink::adapter::runtime {

///  Forces Skyrim's `bAlwaysActive:General` runtime setting on, so gameplay
///  continues while the DovahLink window has focus instead of pausing.
///  Logs and returns without effect if the setting cannot be resolved.
void ApplyAlwaysActiveSetting();

///  Installs a runtime patch that makes achievements eligible with SKSE
///  plugins loaded, following the technique originally ported into the
///  retired native Bridge (see this function's own definition for the full
///  attribution record). Logs and returns without effect if the patch
///  target's Address Library ID is unmapped for the current runtime. If the
///  ID is mapped but the installed Address Library database is missing or
///  incompatible, CommonLib itself fails process startup before this
///  function is reached; that failure mode is inherent to every
///  Address-Library-resolved symbol in this plugin.
void InstallAchievementCompatibilityPatch();

} //  namespace dovahlink::adapter::runtime

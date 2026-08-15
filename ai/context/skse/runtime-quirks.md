# SKSE / Skyrim runtime quirks

Real, empirically-confirmed behavior of SKSE and Skyrim that is not (or not clearly) documented
publicly, discovered while building DovahLink. This is not a style or architecture guide; it is a
record of surprises so the next person debugging a similar symptom does not have to rediscover them
from scratch.

Record a new entry here whenever a manual verification pass (see `testing.md`'s "Manual
verification" section) turns up a genuine SKSE or engine quirk, not just a project-specific bug.
Each entry should say what was observed, why, the fix or workaround, and where.

## One `MessagingInterface::RegisterListener` call per plugin

**Observed:** a plugin that calls `SKSE::MessagingInterface::RegisterListener` more than once (e.g.
once for existing startup logic, once more for a new concern) gets both calls silently rejected.
SKSE logs `Failed to register messaging listener for SKSE` for each attempt and the whole plugin is
disabled by SKSE's loader ("fatal error occurred while loading plugin"), with no indication which
call, or that there even were two, caused it.

**Fix:** route every message type through the one listener already registered, dispatching on
`message->type` inside its own callback, rather than registering a second listener.

**Where:** `bridge/plugin/dovahlink_bridge_plugin.cpp`, the single `messaging->RegisterListener(...)`
call. Found: 2026-08-14.

## `SKSE::Init` must run before any interface registration

**Observed:** `SKSE::MessagingInterface::RegisterListener`, `SKSE::SerializationInterface`'s
callback setters, and any other `SKSE::Get*Interface()`-based registration depend on
`SKSE::GetPluginHandle()` internally. Until `SKSE::Init(skse)` has run, that returns an unset
sentinel (`static_cast<PluginHandle>(-1)` in CommonLibSSE-NG's `APIStorage`), and every such
registration silently fails — same symptom as the listener quirk above (`Failed to register
messaging listener for SKSE`, plugin disabled), for a completely different reason. A plugin can
reach this state having never called `SKSE::Init` at all if it only used `skse->QueryInterface(...)`
directly (which does not require it) for everything, and never happened to call anything that goes
through the `SKSE::` free-function API layer.

**Fix:** call `SKSE::Init(skse)` early in `SKSEPluginLoad`, before any interface-registration call.

**Where:** `bridge/plugin/dovahlink_bridge_plugin.cpp`, `SKSEPluginLoad`. Found: 2026-08-14.

## `kPostLoadGame`'s data is a value, not a pointer

**Observed:** `SKSE::MessagingInterface::Message::data` for `kPostLoadGame` is documented (e.g.
skyrim.dev) only as "a `bool` indicating success/failure," which reads as "a pointer to a bool."
It is not. SKSE stuffs the boolean directly into the pointer's own bit pattern
(`reinterpret_cast<void*>(success)`), the same trick many C APIs use to pass a small value through a
`void*` parameter without an allocation. Dereferencing it as `*static_cast<const bool*>(data)`
crashes: for `success == true`, that's a read of address `0x1`, immediately and reliably.

**Fix:** compare the pointer's value directly (`data != nullptr`) rather than dereferencing it.

**Where:** `bridge/application/game_lifecycle_tracker.hpp`/`.cpp`, `DecodePostLoadGameSuccess`.
Found: 2026-08-14, via a real crash dump showing address `0x1` being read.

## A corrupted save crashes past every handler, including ours

**Observed:** deliberately corrupting a save file (scrambling bytes in the middle of a copy) and
attempting to load it crashed Skyrim instantly, with no dialog from SKSE, from DovahLink, or from a
third-party crash-logging mod — and with none of DovahLink's own lifecycle events (not even
`kPreLoadGame`) having fired first. This looks like a low-level engine fail-fast (the kind
`__fastfail`/security-cookie checks produce) triggered by a save-format sanity check, which is
deliberately designed to bypass every exception handler, including SEH-based crash loggers, as a
defense against exploiting corrupted-input bugs.

**Fix/implication:** none applicable at the DovahLink level — this happens before SKSE ever
dispatches a load-related message, so there is nothing for a plugin to intercept. Do not assume a
severely corrupted save reliably produces a clean `kPostLoadGame(false)`; for some corruption
severities there may be no SKSE-visible signal at all, only the process disappearing.

**Where:** discovered during Phase 2's lifecycle empirical verification pass; no corresponding code
location. Found: 2026-08-14.

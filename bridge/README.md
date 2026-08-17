# Skyrim bridge

The native SKSE bridge reads supported Skyrim game state and exposes it to one authenticated
loopback client through the DovahLink protocol. See
[`ai/context/skse/architecture.md`](../ai/context/skse/architecture.md) for its internal
boundaries and [`ARCHITECTURE.md`](../ARCHITECTURE.md) for how it fits the rest of the project.

This document records the toolchain, dependency, and reuse decisions used for the published Phase 2
bridge release, version `0.2.0`. It is the human-readable record; the pins below are enforced by
`vcpkg.json`, `vcpkg-configuration.json`, and `CMakePresets.json`.

## Supported runtime

Bridge version `0.2.0` supports exactly one runtime: Steam Skyrim `1.6.1170` with SKSE `2.2.6`. Every other
runtime (including `1.5.97`, GOG, and VR) is rejected during plugin initialization. See
[`PRODUCT.md`](../PRODUCT.md) and [`ARCHITECTURE.md`](../ARCHITECTURE.md) for this scope.

## Toolchain

| Tool | Pinned version | Notes |
|---|---|---|
| Compiler | MSVC 2022, v143 toolset (VS 17.14) | Confirmed installed on the reference dev machine via `vswhere`. |
| Language standard | C++23 | `/std:c++23`. |
| Build generator | CMake Presets | Presets file defines the configure/build/test presets used by both local builds and CI. |
| CMake | 4.4.2 | Latest stable release at the time this baseline was recorded. |
| Build tool | Ninja 1.13.2 | Latest stable release at the time this baseline was recorded. |
| Package manager | vcpkg, manifest mode | Builtin baseline pinned below; classic mode is not used. |

`.github/workflows/bridge-ci.yml` installs CMake `4.4.2` and Ninja `1.13.2` exactly via Chocolatey
on `windows-2022`, and sets up the MSVC 2022 developer environment before configuring. A local
dev machine should match these same versions for a reproducible build.

Visual Studio 2022 installations with the Desktop development with C++ workload bundle CMake,
Ninja, `vcvarsall.bat`, and vcpkg, but do not place all of them on `PATH` by default.
`integration/run-scenarios.ps1` uses Visual Studio Installer's `vswhere.exe` to find a complete
Community, Professional, or Enterprise installation, imports its x64 developer environment, and
then configures with `cmake --preset windows-x64-debug`.

## Dependency baselines

One dependency is not vcpkg-managed: `bcrypt.lib`, the Windows SDK import library for CNG
(`<bcrypt.h>`), used only for `BCryptGenRandom` in `bridge/security/csprng.cpp`. It ships with the
Windows SDK already required by MSVC 2022 and is not a new external dependency; it exists because
`ai/context/protocol/security.md` requires token and session material to come from Windows CNG,
never `std::random_device`.

vcpkg builtin registry baseline (`microsoft/vcpkg`):

- Commit: `2f1d605400c8727cc00c15797aba796c88ccd523`

Pinned package versions resolved at that baseline:

| Package | Version |
|---|---|
| Boost (Asio, Beast, JSON — one release for all three) | `1.91.0` |
| Catch2 | `3.15.3` |

CommonLibSSE-NG (`commonlibsse-ng-flatrim`, SE/AE-capable, VR excluded) comes from the
Color-Glass Studios vcpkg registry, not the vcpkg builtin registry:

- Registry: `https://gitlab.com/colorglass/vcpkg-colorglass`
- Registry baseline commit: `6309841a1ce770409708a67a9ba5c26c537d2937` (registry `main`, last
  updated 2023-05-13; the registry has not changed since)
- Port version: `commonlibsse-ng-flatrim` `3.7.0#0`
- Upstream source the port builds: `CharmedBaryon/CommonLibSSE` at commit
  `c4ab853d095e81e3390b282d7ba01ab2f24ebf25`
- Port build options: `ENABLE_SKYRIM_VR=off`, `SKSE_SUPPORT_XBYAK=on`
- Transitive port dependencies: `fmt`, `rapidcsv`, `spdlog`, `xbyak`
- CMake integration, confirmed against the pinned port and upstream source rather than assumed:
  `find_package(CommonLibSSE CONFIG REQUIRED)` exposes the target `CommonLibSSE::CommonLibSSE`. The
  port also installs a `CommonLibSSE.cmake` helper module providing `add_commonlibsse_plugin(...)`,
  which is documented to auto-generate the `SKSEPluginInfo` metadata block -- **do not use it at
  this pinned commit.** Its generated `__<target>Plugin.cpp` contains `#include "REL/Relocation.h"`
  before `#include "SKSE/SKSE.h"`, but at the pinned upstream commit, `REL/Relocation.h` requires
  namespace aliases (`REL::stl`, `REL::WinAPI`) that are only established partway through
  `SKSE/SKSE.h`'s own header chain (`SKSE/Impl/PCH.h`) -- confirmed directly against the vendored
  source, and by reproducing the resulting cascade of "'stl' is not a class or namespace" / missing
  `std::span` / invalid `sv`-literal errors when actually built. `bridge/plugin/
  dovahlink_bridge_plugin.cpp` includes `SKSE/SKSE.h` first (the order `PCH.h` actually requires)
  and declares the `SKSEPluginInfo` block itself instead of using the macro; see that file and its
  CMakeLists.txt target for the working pattern.
- Player level: `RE::PlayerCharacter::GetSingleton()` (declared in `RE/P/PlayerCharacter.h`,
  reachable via the umbrella header `RE/Skyrim.h`) inherits `GetLevel() const` (returns
  `std::uint16_t`) from `RE::Actor`, confirmed by reading both headers at the pinned commit.

No dependency here duplicates a role already covered by Boost, Catch2, or CommonLibSSE-NG's own
dependencies; no second JSON, WebSocket, logging, cryptography, or test library is introduced.

`bridge/vcpkg.json` declares each of these pinned versions incrementally, in the same step that
first consumes it, rather than all at once: `catch2` landed with the build scaffolding, `boost-json`
with the bounded JSON decoder, `boost-asio` with the loopback listener, `boost-beast` with WebSocket
framing, and `commonlibsse-ng-flatrim` (via `bridge/vcpkg-configuration.json`) lands with the
game-state adapter.
This keeps every step's build from compiling dependencies it doesn't use yet.

## Default loopback port

The DovahLink bridge listens on TCP port `58231` by default, overridable through a documented
configuration value that changes only the port, never the listening address (`127.0.0.1` and
`::1` only — see `ai/context/protocol/security.md`). `58231` was chosen from the IANA dynamic/
private range (49152-65535) to avoid collision with common local dev-tool ports (3000, 5173,
8000, 8080, 9000) and any Skyrim-related tooling's default ports.

## Developer-token authentication (optional)

`DOVAHLINK_BRIDGE_TOKEN` is a developer-only authentication path, per
[`ai/context/protocol/security.md`](../ai/context/protocol/security.md)'s "Developer authentication".
**Normal users never set this variable.** They authenticate through the in-game pairing flow instead,
and pairing works identically whether or not this variable is set -- it exists only so a developer can
open a `one_time_local_token` session without going through pairing.

When present, the bridge reads it from the environment at plugin load, hex-encoded (exactly 64
lowercase-or-uppercase hex characters, no `0x` prefix, no separators). Hex was chosen over base64 to
avoid pulling in an encoding dependency this codebase does not otherwise need and to keep the value
trivially assembled by a developer launch script using a cryptographically secure generator:

```powershell
$tokenBytes = [byte[]]::new(32)
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($tokenBytes)
    $env:DOVAHLINK_BRIDGE_TOKEN = [BitConverter]::ToString($tokenBytes).Replace("-", "").ToLowerInvariant()
}
finally {
    $rng.Dispose()
    [Array]::Clear($tokenBytes, 0, $tokenBytes.Length)
}
```

Plugin startup never fails because of this variable; it only affects whether developer-token
authentication is available:

- **Unset (the normal case):** the bridge loads normally, logs that developer-token authentication is
  disabled, and pairing is unaffected.
- **Set but malformed** (empty, not valid hex, or not exactly 32 decoded bytes): the bridge loads
  normally, logs a warning naming the required format, and disables only developer-token
  authentication. Pairing is unaffected.
- **Set and valid:** developer-token authentication is enabled for the bridge's lifetime.

The bridge never logs the token's value in any of these cases. The decoded bytes are handed directly
to `security::TokenStore` (`bridge/security/token_store.hpp`), which owns clearing them after
consumption or expiry; a missing or malformed value hands it an empty token, which `TokenStore` treats
as permanently unavailable. The required token length, source, and failure behavior are defined in
[`ai/context/protocol/security.md`](../ai/context/protocol/security.md).

### Known limitation: no reconnect after a successful session, within one bridge lifetime

`TokenStore` has no awareness of session lifecycle: committing a matching reservation marks the token consumed
permanently, and nothing -- not `RunConnectionSession`'s cleanup, not a disconnect, not a
timeout -- ever makes it available again. This is deliberate, not an oversight, but it has a real
consequence: once one connection successfully authenticates, no later connection -- the same
client reconnecting after a network blip or app restart, or any other client -- can ever
authenticate again until the bridge process itself restarts with a freshly generated token. A
connection attempt that fails *before* session admission (wrong token, version mismatch, another
active session, or internal setup failure) does not spend it, so a retry after a failed attempt
still works; only a retry after a *successful* session does not. See `ROADMAP.md`'s Phase 3,
Local Device Pairing and Reconnection, for the
planned fix -- a separate, device-scoped credential issued after a successful pairing, stored on
the client, so a reconnect never needs the one-time bootstrap token again. That phase leaves this
token's semantics exactly as documented here.

## SkyrimWebSocket reference

- Reference release: [`v1.15.1`](https://github.com/andreyvelsk/SkyrimWebSocket/releases/tag/v1.15.1)
- Resolved immutable commit: `3d42b908b6060774f3da68f53ed7107b914c740d`
- License: MIT, copyright (c) 2026 andreyvelsk. Any adopted or adapted source retains its
  original MIT notice.

### Adopt / adapt / exclude evaluation

Evaluated directly against the pinned commit's source
(`plugin.cpp`, `src/server/WsServer.*`, `src/server/WsSession.*`, `src/server/MessageRouter.h`,
`src/game/EventBus.*`, `src/game/PlayerReader.cpp`, `CMakeLists.txt`, `vcpkg.json`, `PROTOCOL.md`,
`SkyrimWebSocket.ini.example`).

| Part | Decision | Reason |
|---|---|---|
| `WsSession`'s Beast session shape: `websocket::stream`, `flat_buffer`, and asynchronous I/O | **Adapt** | DovahLink rewrites the session around `websocket::stream<tcp_stream>`, bounded input, cancellation, and operation deadlines. Phase 1 remains linear with one operation at a time; the reference `deque<string>` write queue and `writing_` flag are not adopted, because outbound queueing belongs to Phase 4. |
| `WsServer`'s acceptor loop (construct `tcp::acceptor` on an explicit endpoint, async-accept, hand the socket to a session) | **Adapt** | Sound minimal Asio acceptor pattern. DovahLink adapts it to bind only `127.0.0.1`/`::1` (never a configurable address) and to enforce the one-connected-client limit at accept time, which this reference does not do. |
| `EventBus::Install()` registering SKSE event sinks once after `kDataLoaded` and reacting to engine events instead of polling | **Adapt** | The registration technique (install a sink once, on the game thread, after data is loaded) is the right shape for registering `RE::LevelIncrease::Event`. The surrounding machinery — a generic per-key version counter and a shared resolver cache (`EventBus::CachedValue`, `ResolveCached`) for arbitrary polled fields — is excluded: it is a generic event/caching framework built to support many polled fields, and DovahLink has exactly one push-only event with no polling to optimize. |
| `PlayerReader::ReadLevel()`'s call, `RE::PlayerCharacter::GetSingleton()->GetLevel()` | **Adapt** | Confirms the correct CommonLib API for reading the player's level. Reused as a technique inside DovahLink's own level adapter; not copied, since the surrounding function returns `nlohmann::json` for a generic polled-field system DovahLink doesn't have. |
| `CMakeLists.txt`'s general SKSE-plugin shape (`target_compile_features(cxx_std_23)`, `target_precompile_headers`, copy-to-mods-folder post-build step) | **Adapt** | Confirms a working C++23 CommonLib-plugin CMake shape. DovahLink adapts the compile-feature and PCH lines; the automatic copy-into-the-Skyrim-install step is deliberately not reused (see Exclude below) and CommonLibSSE-NG is consumed as a pinned vcpkg port, not a git submodule as this reference does. |
| Wire protocol: flat `{"type": ..., "id": ...}` messages, client-declared push frequency, `sendOnChange` | **Exclude** | The canonical DovahLink protocol (`protocol/schema/README.md`) is the only cross-side contract; the reference wire protocol is not adopted. |
| `command` message family: `equip`, `unequip`, `use`, `read_book`, `drop`, `favorite*`, hotkey and quest mutation, `player_marker_set`, `fast_travel` | **Exclude** | Mutable game commands are outside the read-only Phase 1 scope defined by `PRODUCT.md` and `ARCHITECTURE.md`. |
| JSON library: `nlohmann-json` | **Exclude** | The bridge uses Boost.JSON for protocol data; adopting a second JSON library would require a new maintainer decision. |
| `[Server] ListenAddress` INI key, default `127.0.0.1` but documented to accept `0.0.0.0` for "remote debugging" | **Exclude** | Wildcard/LAN configuration. DovahLink's only configurable transport value is the port; the listening address is never configurable (`ai/context/protocol/security.md`). |
| Authentication: none — any peer that reaches the port can subscribe, query, or send commands | **Exclude** | DovahLink requires one-time token authentication before any `hello_ack`. This reference has nothing to adapt here; the token/session design is built fresh against `protocol/schema/README.md` and `ai/context/protocol/security.md`. |
| Global static ownership (`g_ioc`, `g_server`, `g_ioThread`, `g_workGuard` as file-scope statics in `plugin.cpp`, detached I/O thread, no shutdown listener) | **Exclude** | DovahLink requires one coordinator owning registrations, queues, workers, and transport, with the full documented shutdown barrier (`ai/context/skse/architecture.md`) — this reference has no equivalent shutdown path to adapt from. |
| Crash-handler installation (`SetUnhandledExceptionFilter`, minidump writing) gated on log level | **Exclude** | Crash-handler installation is outside the documented Phase 1 scope. |
| Per-subscription polling at a client-declared frequency (minimum 50 ms) as the mechanism for delivering any field, including level | **Exclude** | DovahLink publishes level changes from `RE::LevelIncrease::Event` and does not use worker-side game polling. |
| `directxtk`, `rapidcsv` dependencies | **Exclude** | Unrelated to DovahLink's Phase 1 scope (no rendering overlay, no CSV-driven data); not pulled in. |
| `FieldRegistry` / per-field-key generic resolver system (`GameReader`, `InventoryReader`, `MagicReader`, `HotkeyReader`, `QuestReader`) | **Exclude** | A general key-addressable field API is exactly the "generic event framework or speculative service layer" `ai/context/skse/architecture.md` and `ARCHITECTURE.md` reject for Phase 1. DovahLink exposes one state area (`character`) through its own protocol mapping, not a generic field registry. |

All adapted code will carry the required MIT attribution for SkyrimWebSocket
(`3d42b908b6060774f3da68f53ed7107b914c740d`) at the point it is introduced.

## Live event delivery is deferred to Phase 4

Phase 1 does not implement an outbound event queue or coalescer. A connected and subscribed client
sees a level change only by asking again -- a fresh `subscribe` or `snapshot_request` -- never as an
unprompted `state_event`. `RunConnectionSession` stays purely request/response: it never writes to a
socket except in direct reply to a message it just read.

This is a deliberate, roadmap-tracked deferral, not an oversight. Delivering an unprompted push
requires a connection to write independent of its own next read, which the current synchronous,
one-operation-at-a-time session loop cannot do without an unsafe concurrent write against
Boost.Beast's "shared objects: unsafe" `websocket::stream`. `WebSocketSession` now runs each
operation through Beast's async API for safe cancellation and timeouts, but its public facade and
`RunConnectionSession` remain deliberately linear. The full-duplex loop, along with the
outbound-lane and rate-class design it enables, is
[`ROADMAP.md`](../ROADMAP.md)'s Phase 4, Live State Synchronization Foundation. The architecture
already agreed for that phase, recorded here so Phase 4 does not have to rediscover it:

- Refactor the linear session facade and `RunConnectionSession` into a full-duplex async loop using
  C++20/23 coroutines (`co_await`): coroutine code stays close to the current linear control flow,
  while the existing executor-owned async transport operations already provide safe cancellation
  and bounded I/O.
  One outstanding async read and one outstanding async write on the same `websocket::stream` is a
  documented-safe pattern; two of either at once, or a blocking call mixed with an async one, is
  not.
- Prefer a single-threaded `io_context` for as long as it holds; only introduce a strand (a
  mechanism that serializes a connection's own async operations across multiple threads) once
  multiple networking threads are actually required, not in advance.
- Separate outbound traffic into `controlMessages`, `reliableEvents`, and `stateUpdates` --
  deliberately not one generic event queue, since a health update is not the same kind of thing as
  a quest completion. `stateUpdates` coalesces raw owned state latest-value-wins per key before
  revision metadata is assigned. `reliableEvents` stays ordered and is never silently overwritten
  or dropped; if it fills, delivery is prioritized over `stateUpdates` and, if a client still
  cannot keep up, that client is marked unhealthy and disconnected rather than buffered
  indefinitely or allowed to block the bridge. Reliable events are scoped to the authenticated
  session: disconnecting discards any events still queued for that session, and reconnecting starts
  from fresh snapshots rather than replaying the discarded queue.
- Publish a value only when it changes. Prefer an existing native Skyrim event for any field that
  has one; fall back to one bridge-owned, per-rate-class-throttled sampling hook only where no
  suitable event exists. A rate class (Fast/Medium/Slow) names a maximum publish frequency, not a
  mandatory one.
- The approved 128-message outbound security bound remains the ceiling unless a separately approved
  protocol-limit change replaces it. How Phase 4 divides that bound among its three categories
  follows profiling once the real delivery mechanism exists, not advance estimation.

## Optional trust-administration console adapter

The bridge always registers native Papyrus functions for listing, revoking, and resetting trusted
clients (`bridge/game_state/commonlib_trust_admin_papyrus_adapter.cpp`). Reaching them from
Skyrim's in-game console requires a separate, optional integration
([`console-admin/README.md`](../console-admin/README.md)) with a third-party plugin, ConsoleUtil
Extended — not part of this bridge's own dependency baseline above, and not required for any other
bridge behavior.

## Manual verification record template

Use this template to record real Skyrim verification for a release. Nothing here can substitute for
that run: it requires a real Skyrim process, which no automated test in this repository uses (per
`ai/context/skse/testing.md`). Copy this section, fill in every field from an actual run using
`integration/DovahLinkValidationClient` (see `integration/README.md`), and keep the completed record
with the release evidence.

```text
Date:
Skyrim runtime and distribution:
SKSE version:
Address Library version:
CommonLibSSE-NG pinned commit (bridge/README.md's "Dependency baselines"):
Bridge build (git commit / branch):
Compiler, CMake, Ninja versions used for this build:
Load order and mod-manager conditions:

Installation and launch steps taken:

Expected plugin startup behavior:
Observed plugin startup behavior:

Initial level snapshot result (expected vs. observed):

Level-increase result, observed by requesting a fresh snapshot after a level-up
(unprompted push delivery is Roadmap Phase 4, not part of this record):

Disconnect result (expected vs. observed):
Reconnect result for an attempt made BEFORE the one-time token was consumed
(expected vs. observed). Reconnecting a client that already completed one
successful session requires restarting the bridge -- record that this was
not exercised, per Roadmap Phase 3:

Stale-state behavior (expected vs. observed):

Invalid token behavior (expected vs. observed):
Expired token behavior (expected vs. observed):
Reused token behavior (expected vs. observed):

Clean Skyrim shutdown result (expected vs. observed):

Known limitations observed during this run:
```

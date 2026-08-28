# Skyrim bridge

The native SKSE bridge reads supported Skyrim game state and exposes it to one authenticated
loopback client through the DovahLink protocol. See
[`ai/context/skse/architecture.md`](../ai/context/skse/architecture.md) for its internal
boundaries and [`ARCHITECTURE.md`](../ARCHITECTURE.md) for how it fits the rest of the project.

This document records the toolchain, dependency, and reuse decisions used for the historical Bridge
baseline, version `0.3.2`. It is not a supported public release or compatibility target. The pins
below are enforced by
`vcpkg.json` and `vcpkg-configuration.json`; `CMakePresets.json` selects the vcpkg toolchain and
target triplet used by the build.

## Supported runtime

Bridge version `0.3.2` supports exactly one runtime: Steam Skyrim `1.6.1170` with SKSE `2.2.6`. Every other
runtime (including `1.5.97`, GOG, and VR) is rejected during plugin initialization. See
[`PRODUCT.md`](../PRODUCT.md) and [`ARCHITECTURE.md`](../ARCHITECTURE.md) for this scope.

The Windows build targets Windows 10 APIs and supports Windows 10 and later. The plugin rejects
older Windows runtimes during initialization.

## Toolchain

| Tool | Pinned version | Notes |
|---|---|---|
| Compiler | MSVC 2022, v143 toolset (VS 17.14) | Confirmed installed on the reference dev machine via `vswhere`. |
| Language standard | C++23 | `/std:c++23`. |
| Build generator | CMake Presets | Presets file defines the configure/build/test presets used by both local builds and CI. |
| CMake | 4.4.2 | Latest stable release at the time this baseline was recorded. |
| Build tool | Ninja 1.13.2 | Latest stable release at the time this baseline was recorded. |
| Package manager | vcpkg, manifest mode | Builtin baseline pinned below; classic mode is not used. |

`.github/workflows/bridge-ci.yml` and `.github/workflows/integration-ci.yml` install CMake `4.4.2`
from a pinned, hash-verified direct download of Kitware's official GitHub release, verify Ninja
`1.13.2` is already present on `windows-2022` (the image ships that exact version, so nothing
installs it), and set up the MSVC 2022 developer environment before configuring. A local dev
machine should match these same versions for a reproducible build. Both workflows also cache the
checked-out and bootstrapped vcpkg tooling (see "Dependency baselines" below), keyed on its pinned
commit, and the checked-out colorglass registry, keyed on the content hash of
`bridge/vcpkg-configuration.json` rather than a literal commit -- a cache miss still fetches the
registry normally, but a cache hit avoids repeatedly re-fetching it from GitHub or GitLab.

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
| GoogleTest/GoogleMock (test-only, Catch2 target integration) | `1.18.0` |

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

No dependency here duplicates a production role already covered by Boost or CommonLibSSE-NG's own
dependencies. GoogleTest/GoogleMock is test-only and provides the Bridge's mock-based contract
testing; controllable thread-safe fakes remain the required double when timing, lifetime,
synchronization, or mutable state is under test.

`bridge/vcpkg.json` declares each of these pinned versions incrementally, in the same step that
first consumes it, rather than all at once: `catch2` landed with the build scaffolding, `boost-json`
with the bounded JSON decoder, `boost-asio` with the loopback listener, `boost-beast` with WebSocket
framing, `gtest` with the C1 cross-thread contract test, and `commonlibsse-ng-flatrim` (via
`bridge/vcpkg-configuration.json`) lands with the game-state adapter.
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
permanently, and nothing -- not `ConnectionSession::Run`'s cleanup, not a disconnect, not a
timeout -- ever makes it available again. This is deliberate, not an oversight, but it has a real
consequence: once one connection successfully authenticates, no later connection -- the same
client reconnecting after a network blip or app restart, or any other client -- can ever
authenticate again until the bridge process itself restarts with a freshly generated token. A
connection attempt that fails *before* session admission (wrong token, version mismatch, another
active session, or internal setup failure) does not spend it, so a retry after a failed attempt
still works; only a retry after a *successful* session does not. See [Stage 3 roadmap](../roadmap/03-local-device-pairing-and-reconnection.md),
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
| `FieldRegistry` / per-field-key generic resolver system (`GameReader`, `InventoryReader`, `MagicReader`, `HotkeyReader`, `QuestReader`) | **Exclude** | A general key-addressable field API is exactly the "generic event framework or speculative service layer" `ai/context/skse/architecture.md` and `ARCHITECTURE.md` reject for Phase 1. DovahLink exposes registered state areas through their own protocol mappings, not a generic field registry. |

All adapted code will carry the required MIT attribution for SkyrimWebSocket
(`3d42b908b6060774f3da68f53ed7107b914c740d`) at the point it is introduced.

## Live event delivery is deferred to Phase 4

The current transitional contract does not register a state area, so `subscribe` and
`snapshot_request` reject state requests and no state snapshot is produced. Phase 1 did not
implement an outbound event queue or coalescer; registered progression domains and unprompted
`state_event` delivery are Stage 4 work. `ConnectionSession::Run` remains purely request/response
until that phase's full-duplex transport work lands.

This is a deliberate, roadmap-tracked deferral, not an oversight. Delivering an unprompted push
requires a connection to write independent of its own next read, which the current synchronous,
one-operation-at-a-time session loop cannot do without an unsafe concurrent write against
Boost.Beast's "shared objects: unsafe" `websocket::stream`. `WebSocketSession` now runs each
operation through Beast's async API for safe cancellation and timeouts, but its public facade and
`ConnectionSession::Run` remain deliberately linear. The full-duplex loop, along with the
outbound-lane and rate-class design it enables, is
[Stage 4 roadmap](../roadmap/04-live-state-synchronization-foundation.md), Live State Synchronization Foundation. The architecture
already agreed for that phase, recorded here so Phase 4 does not have to rediscover it:

- Refactor the linear session facade and `ConnectionSession::Run` into a full-duplex async loop using
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

## Production capture and lifecycle composition without a live registered domain

The production composition root (`bridge/plugin/dovahlink_bridge_plugin.cpp`) wires the full capture,
worker-handoff, and publication-routing chain -- `CadenceScheduler`, `CapturePolicyRegistry`,
`ActivePlayContextProvider`, `RegisteredStateAreaPolicy`, `ActiveSessionPublicationRouter`,
`StatePublisher`, `CommonLibCaptureQueueDiagnostics`, `CaptureDispatchWorker`, and
`CadenceTickDriver` -- while zero state areas are registered and no per-session queue is ever
attached to `ActiveSessionPublicationRouter` in production. `CadenceTickDriver` consumes the policy
registry to reject due keys that are absent or not complete sampled policies; no sampled key is
registered in 4.2 because the first real value reader belongs to Phase 4.3.
This is deliberate: no protocol state
area exists yet (`protocol/schema/README.md`'s "Registered state areas" retired the previous
`character` aggregate and registers nothing new), so there is nothing real to publish. The mechanism
is still built and tested against a real production graph so a later phase registers a domain into
working infrastructure rather than co-designing the infrastructure and the first domain at the same
time. There is no process-lifetime revision tracker in this graph: `StatePublisher` reaches a
revision tracker per call, through whichever `PlayContext` a capture was pinned against
(`activePlayContextProvider`), so a new save/new game starts with a completely fresh, empty revision
sequence rather than inheriting the previous context's.

`RegisteredStateAreaPolicy` (`bridge/application/registered_state_area_policy.hpp`) owns the fixed
8-slot bound (`kMaxRegisteredStateAreas`, `bridge/application/constants.hpp`) documented in
`ai/context/protocol/security.md`'s "Input limits". `ActiveSessionPublicationRouter`
(`bridge/application/active_session_publication_router.hpp`) is constructed with nothing attached and
stays that way for the rest of 4.2; `SessionPublicationFactory`
(`bridge/application/session_publication_factory.hpp`), which would attach a real session's
`BoundedOutboundQueue` to it after authentication, is constructed and injected into the composition
root but has no call site yet -- that lands with the full-duplex session integration that replaces
`ConnectionSession`'s current single-operation writer.

`CaptureDispatchWorker` (`bridge/application/capture_dispatch_worker.hpp`) and `CadenceTickDriver`
(`bridge/application/cadence_tick_driver.hpp`) both start after `kDataLoaded` and stop/join during
coordinator shutdown, but carry no traffic in 4.2: no sampled key is registered with the scheduler,
so `CadenceTickDriver`'s per-tick `ICadenceScheduler::DueKeys` call returns an empty set every time,
and no native-event adapter enqueues into `CaptureDispatchWorker`. Their unit and composition tests
are the only proof of correct behavior until a later phase registers a real domain and exercises them
under production load.

### Stage 5 limitations not solved by this design

Three criteria remain genuinely open, named explicitly rather than left implicit:

- **`SessionPublicationFactory` is constructed and injected but has no caller.** The full-duplex
  session integration that would call `CreateForSession` after authentication is Stage 6 scope.
- **Reliable native-Event loss under capture-queue pressure is diagnosed, not prevented or
  recovered.** `CaptureDispatchWorker::TryEnqueue` reports a mode-aware rejection through
  `ICaptureQueueDiagnostics` (Event-mode logged as an error, Snapshot-mode as a routine warning,
  since the next sample tick recaptures current state anyway), but a rejected item never reaches the
  worker's ordering point, so its play context's authoritative store is never updated for that value
  either. This has no production consequence today (the queue-full path is unreachable with zero
  registered state areas), but the recovery contract -- bounded retry, a dirty marker, or accepting
  the loss -- is undecided and must be revisited once Phase 4.3 registers a real Event-mode domain.
- **Stale-context publication is minimized, not eliminated.** `CaptureDispatchWorker::Dispatch` and
  `StatePublisher::PublishCapture` check the active play context immediately before a built
  publication reaches the sink, and the envelope carries the captured context's own `playContextId`,
  but a context transition landing in that last instant can still let a stale publication reach the
  router, correctly labeled rather than mislabeled. Full elimination requires a send-time
  `playContextId` check against the live session's own current context -- that check belongs to
  Stage 6's authenticated-session writer, which does not exist yet.

## Optional trust-administration console adapter

The bridge attempts to register native Papyrus functions for listing all known devices, listing
trusted or blocked devices, showing help, revoking, resetting, and managing known devices
(`bridge/game_state/commonlib_trust_admin_papyrus_adapter.cpp`). Reaching them from Skyrim's
in-game console requires a separate, optional integration
([`console-admin/README.md`](../console-admin/README.md)) with a third-party plugin, ConsoleUtil
Extended — not part of this bridge's own dependency baseline above, and not required for any other
bridge behavior.

## Runtime compatibility options

The bridge reads an optional `Data/SKSE/Plugins/DovahLinkBridge.ini` at startup for two independent
compatibility toggles, both enabled by default:

```ini
[DovahLink]
bAlwaysActive=1
bAchievementCompat=1
```

A missing file, a missing key, or a value other than `0`/`1` falls back to that key's own default
rather than failing plugin load; see `bridge/application/game_behavior_config.hpp`.

**`bAlwaysActive`** forces Skyrim's own `bAlwaysActive:General` setting on at startup
(`RE::INISettingCollection`), so the game keeps running while the DovahLink window has focus instead
of pausing -- required so the pairing code and the companion app stay usable while Skyrim is
unfocused. This replaces the third-party "Skyrim Always Active" mod workaround previously documented
in `TROUBLESHOOTING.md`.

**`bAchievementCompat`** installs a runtime patch making achievements eligible with SKSE plugins
loaded. This is a deliberate, maintainer-approved exception to `ai/context/skse/cpp-style.md`'s
"minimize hooks" guidance and to this bridge's read-only-first default: it restores an engine-level
eligibility flag, not a companion feature, and does not touch the DovahLink protocol, transport, or
any state exposed to a client. The technique -- filling the target function with `REL::INT3`, then
overwriting its start with an `xor rax, rax; ret` patch generated via Xbyak -- and its two Address
Library IDs (SE `13647`, AE/current-runtime `441528`) are adapted from
[`aers/EngineFixesSkyrim64`](https://github.com/aers/EngineFixesSkyrim64), translated to this pinned
CommonLibSSE-NG's `REL::safe_fill`/`REL::safe_write` free functions, which take the place of that
reference's `Relocation::write_fill`/`write` member functions:

- Resolved commit: `c37a8041ffc0a5859e78a19c71b877327773455d`
- License: MIT. Any adopted or adapted source retains its original MIT notice.

Both toggles are applied once, early in `SKSEPluginLoad`, before `kDataLoaded`; see
`bridge/game_state/commonlib_game_behavior_compatibility.cpp`.

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

Always-active behavior with bAlwaysActive=1 (expected: Skyrim keeps running while unfocused;
observed):
Achievement compatibility with bAchievementCompat=1 (expected: achievements remain eligible with
SKSE plugins loaded, verified via Steam; observed):
Both toggles disabled via DovahLinkBridge.ini (expected: prior default Skyrim behavior for both;
observed):

Initial state snapshot result (not applicable while no state area is registered):

Registered-domain progression result (deferred until Stage 4 registers the first production domains):

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

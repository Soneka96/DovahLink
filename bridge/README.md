# Skyrim bridge

The native SKSE bridge reads supported Skyrim game state and exposes it to one authenticated
loopback client through the DovahLink protocol. See
[`ai/context/skse/architecture.md`](../ai/context/skse/architecture.md) for its internal
boundaries and [`ARCHITECTURE.md`](../ARCHITECTURE.md) for how it fits the rest of the project.

This document records the Phase 1 toolchain, dependency, and reuse decisions required before any
bridge code is written. It is the human-readable record; the pins below are enforced by
`vcpkg.json`, `vcpkg-configuration.json`, and `CMakePresets.json` once the build is established.

## Supported runtime

Phase 1 supports exactly one runtime: Steam Skyrim `1.6.1170` with SKSE `2.2.6`. Every other
runtime (including `1.5.97`, GOG, and VR) is rejected during plugin initialization. See
[`ARCHITECTURE.md`](../ARCHITECTURE.md) and `TASK.md` for the approval behind this scope.

## Toolchain

| Tool | Pinned version | Notes |
|---|---|---|
| Compiler | MSVC 2022, v143 toolset (VS 17.14) | Confirmed installed on the reference dev machine via `vswhere`. |
| Language standard | C++23 | `/std:c++23`. |
| Build generator | CMake Presets | Presets file defines the configure/build/test presets used by both local builds and CI. |
| CMake | 4.4.2 | Latest stable release at the time this baseline was recorded. |
| Build tool | Ninja 1.13.2 | Latest stable release at the time this baseline was recorded. |
| Package manager | vcpkg, manifest mode | Builtin baseline pinned below; classic mode is not used. |

## Dependency baselines

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

No dependency here duplicates a role already covered by Boost, Catch2, or CommonLibSSE-NG's own
dependencies; no second JSON, WebSocket, logging, cryptography, or test library is introduced.

## Default loopback port

The DovahLink bridge listens on TCP port `58231` by default, overridable through a documented
configuration value that changes only the port, never the listening address (`127.0.0.1` and
`::1` only — see `ai/context/protocol/security.md`). `58231` was chosen from the IANA dynamic/
private range (49152-65535) to avoid collision with common local dev-tool ports (3000, 5173,
8000, 8080, 9000) and any Skyrim-related tooling's default ports.

## SkyrimWebSocket reference

- Reference release: [`v1.15.1`](https://github.com/andreyvelsk/SkyrimWebSocket/releases/tag/v1.15.1)
- Resolved immutable commit: `3d42b908b6060774f3da68f53ed7107b914c740d`
- License: MIT, copyright (c) 2026 andreyvelsk. Any adopted or adapted source retains its
  original MIT notice per `TASK.md`.

### Adopt / adapt / exclude evaluation

Evaluated directly against the pinned commit's source
(`plugin.cpp`, `src/server/WsServer.*`, `src/server/WsSession.*`, `src/server/MessageRouter.h`,
`src/game/EventBus.*`, `src/game/PlayerReader.cpp`, `CMakeLists.txt`, `vcpkg.json`, `PROTOCOL.md`,
`SkyrimWebSocket.ini.example`).

| Part | Decision | Reason |
|---|---|---|
| `WsSession`'s Beast session shape: `websocket::stream` + `flat_buffer` + a `deque<string>` write queue with a `writing_` flag serializing async writes | **Adapt** | A correct, idiomatic Beast idiom for avoiding concurrent writes on one stream. DovahLink's session differs enough (envelope framing, bounded lanes, size/limit enforcement, auth/session state) that the code is rewritten, but the write-serialization technique is reused. |
| `WsServer`'s acceptor loop (construct `tcp::acceptor` on an explicit endpoint, async-accept, hand the socket to a session) | **Adapt** | Sound minimal Asio acceptor pattern. DovahLink adapts it to bind only `127.0.0.1`/`::1` (never a configurable address) and to enforce the one-connected-client limit at accept time, which this reference does not do. |
| `EventBus::Install()` registering SKSE event sinks once after `kDataLoaded` and reacting to engine events instead of polling | **Adapt** | The registration technique (install a sink once, on the game thread, after data is loaded) is the right shape for registering `RE::LevelIncrease::Event`. The surrounding machinery — a generic per-key version counter and a shared resolver cache (`EventBus::CachedValue`, `ResolveCached`) for arbitrary polled fields — is excluded: it is a generic event/caching framework built to support many polled fields, and DovahLink has exactly one push-only event with no polling to optimize. |
| `PlayerReader::ReadLevel()`'s call, `RE::PlayerCharacter::GetSingleton()->GetLevel()` | **Adapt** | Confirms the correct CommonLib API for reading the player's level. Reused as a technique inside DovahLink's own level adapter; not copied, since the surrounding function returns `nlohmann::json` for a generic polled-field system DovahLink doesn't have. |
| `CMakeLists.txt`'s general SKSE-plugin shape (`target_compile_features(cxx_std_23)`, `target_precompile_headers`, copy-to-mods-folder post-build step) | **Adapt** | Confirms a working C++23 CommonLib-plugin CMake shape. DovahLink adapts the compile-feature and PCH lines; the automatic copy-into-the-Skyrim-install step is deliberately not reused (see Exclude below) and CommonLibSSE-NG is consumed as a pinned vcpkg port, not a git submodule as this reference does. |
| Wire protocol: flat `{"type": ..., "id": ...}` messages, client-declared push frequency, `sendOnChange` | **Exclude** | TASK.md excludes SkyrimWebSocket's wire protocol outright. The canonical DovahLink protocol (`protocol/schema/README.md`) is the only cross-side contract. |
| `command` message family: `equip`, `unequip`, `use`, `read_book`, `drop`, `favorite*`, hotkey and quest mutation, `player_marker_set`, `fast_travel` | **Exclude** | Mutable game commands. Phase 1 is read-only (`PRODUCT.md`, `ARCHITECTURE.md`); TASK.md explicitly excludes them. |
| JSON library: `nlohmann-json` | **Exclude** | TASK.md commits Phase 1 to Boost.JSON as the one JSON library; adopting a second JSON library is explicitly forbidden without a new maintainer decision. |
| `[Server] ListenAddress` INI key, default `127.0.0.1` but documented to accept `0.0.0.0` for "remote debugging" | **Exclude** | Wildcard/LAN configuration. DovahLink's only configurable transport value is the port; the listening address is never configurable (`ai/context/protocol/security.md`). |
| Authentication: none — any peer that reaches the port can subscribe, query, or send commands | **Exclude** | DovahLink requires one-time token authentication before any `hello_ack`. This reference has nothing to adapt here; the token/session design is built fresh against `protocol/schema/README.md` and `ai/context/protocol/security.md`. |
| Global static ownership (`g_ioc`, `g_server`, `g_ioThread`, `g_workGuard` as file-scope statics in `plugin.cpp`, detached I/O thread, no shutdown listener) | **Exclude** | TASK.md explicitly excludes global ownership. DovahLink requires one coordinator owning registrations, queues, workers, and transport, with the full documented shutdown barrier (`ai/context/skse/architecture.md`) — this reference has no equivalent shutdown path to adapt from. |
| Crash-handler installation (`SetUnhandledExceptionFilter`, minidump writing) gated on log level | **Exclude** | TASK.md explicitly excludes crash-handler installation. |
| Per-subscription polling at a client-declared frequency (minimum 50 ms) as the mechanism for delivering any field, including level | **Exclude** | TASK.md excludes worker-side game polling. DovahLink publishes level changes only from `RE::LevelIncrease::Event`. |
| `directxtk`, `rapidcsv` dependencies | **Exclude** | Unrelated to DovahLink's Phase 1 scope (no rendering overlay, no CSV-driven data); not pulled in. |
| `FieldRegistry` / per-field-key generic resolver system (`GameReader`, `InventoryReader`, `MagicReader`, `HotkeyReader`, `QuestReader`) | **Exclude** | A general key-addressable field API is exactly the "generic event framework or speculative service layer" `ai/context/skse/architecture.md` and `ARCHITECTURE.md` reject for Phase 1. DovahLink exposes one state area (`character`) through its own protocol mapping, not a generic field registry. |

All adapted code will carry the required MIT attribution for SkyrimWebSocket
(`3d42b908b6060774f3da68f53ed7107b914c740d`) at the point it is introduced, per `TASK.md`.

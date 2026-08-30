# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `43B9D53C4610462D86EE6C2F28C46B59FF4755389A1B96907AAA703AA0229324`
Phase: Stage 3 — Thin Native Adapter and Private IPC
Package: `plans/stage-3-thin-native-adapter-private-ipc/`
Status: active

## Active concept

- File: `04-process-lifecycle.md`
- Status: pending
- Prerequisites: Stage 1, Stage 2, and Concepts 01-03 complete; feature branch active
- Next action: Implement packaged host launch, endpoint rendezvous, supervision, and deterministic lifecycle handling in Concept 04.

## Completed concepts

- `01-private-ipc-contract.md`: complete. Host and native codecs, opaque event/sample intents, semantic validation, and golden vectors pass the focused checks.
- `02-host-ipc-channel.md`: complete. The host-side loopback TCP listener, framed connection, peer proof, bounded queues, rate limit, availability transitions, reconnect, resynchronization, and failure handling pass focused checks.
- `03-native-adapter-core.md`: complete. The independent native adapter foundation, game-thread marshalling seam, owned bounded handoff, adapter IPC connection, identity, Papyrus status surface, plugin composition, and focused tests are complete. Production Skyrim mappings, outbound capture delivery, and an accepted resynchronization baseline are explicitly deferred to Stage 6.

## Decisions and approved deviations

- The adapter is intentionally thin: host-directed keys/tokens are mapped only
  at the final Skyrim boundary; application logic remains in the host.
- The first production state slice is level-up, coherent fast vitals (health,
  magicka, stamina), medium XP, and slow gold/coins. Fast/medium/slow are host
  cadence categories; the adapter receives only opaque keys or tokens.
- Papyrus is limited to Skyrim-facing command forwarding and host readiness or
  error status; it does not own policy or retry logic.
- Host and adapter are shipped as one atomic package, so private IPC has no
  negotiated protocol-version field; same-package peer/lifetime proof remains
  required. See `DIVERGENCES.md`.

## Deferred debt

- Real Skyrim state mappings, outbound capture delivery, and accepted baseline
  resynchronization remain deferred until Stage 6; they cannot be replaced by
  foundation tests.
- Packaged host launch, endpoint rendezvous, and process lifecycle remain in
  Concept 04.

## Changed files

- `host/DovahLink.Host/Adapter/Ipc/` and
  `host/DovahLink.Host.Tests/Adapter/Ipc/`: no-version C# IPC messages, codecs,
  validation, ownership handling, loopback listener, connection lifecycle,
  rate limiting, and tests.
- `host/DovahLink.Host/Constants.cs`, `host/DovahLink.Host/Enums.cs`, and
  `host/DovahLink.Host.Tests/TestDoubles/FakeClock.cs`: private IPC limits,
  message kinds, and deterministic thread-safe timing support.
- `adapter/ipc/` and `adapter/tests/ipc/`: matching C++ messages, codecs,
  validation, golden vectors, and structural tests.
- `adapter/CMakeLists.txt`, `adapter/CMakePresets.json`, and
  `adapter/vcpkg.json`: minimal independent native contract-test scaffold.
- Phase planning files: Concepts 01–03 completion, approved D1 divergence, the
  first-state-slice decision, and handoff to Concept 04.

## Verification

- `git branch --show-current`: `feature/3-thin-native-adapter-and-private-ipc-continued-2`
- `host/PLAN.md` SHA-256: matches recorded fingerprint
- `dotnet build host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --no-restore`: passed
- `dotnet test host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --no-restore`: 426 passed
- `cmake --build adapter/build/windows-x64-debug --parallel`: passed
- `ctest --test-dir adapter/build/windows-x64-debug --output-on-failure`: 125 passed
- `dotnet format` verification: passed
- `clang-format --dry-run --Werror` verification: passed
- Native CMake's optional `applocal.ps1` dependency-copy helper reported that
  `dumpbin`, `llvm-objdump`, and `objdump` were unavailable; the build still
  returned success and the test executable ran all 125 tests.

## Handoff

Next concept: `04-process-lifecycle.md`
Blocked by: none

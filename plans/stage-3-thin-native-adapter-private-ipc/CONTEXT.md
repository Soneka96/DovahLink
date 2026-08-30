# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `4CFA216D1121044DCB046527AF7ED5A930300EC00934432709DDA575E2020584`
Phase: Stage 3 — Thin Native Adapter and Private IPC
Package: `plans/stage-3-thin-native-adapter-private-ipc/`
Status: active

## Active concept

- File: `02-host-ipc-channel.md`
- Status: complete
- Prerequisites: Stage 1, Stage 2, and Concept 01 complete; feature branch active
- Next action: Implement the native adapter boundary, bounded handoff, and private IPC client in Concept 03.

## Completed concepts

- `01-private-ipc-contract.md`: complete. Host and native codecs, opaque event/sample intents, semantic validation, and golden vectors pass the focused checks.
- `02-host-ipc-channel.md`: complete. The host-side loopback TCP listener, framed connection, peer proof, bounded queues, rate limit, availability transitions, reconnect, resynchronization, and failure handling pass focused checks.

## Decisions and approved deviations

- The adapter is intentionally thin: host-directed keys/tokens are mapped only
  at the final Skyrim boundary; application logic remains in the host.
- Papyrus is limited to Skyrim-facing command forwarding and host readiness or
  error status; it does not own policy or retry logic.
- Host and adapter are shipped as one atomic package, so private IPC has no
  negotiated protocol-version field; same-package peer/lifetime proof remains
  required. See `DIVERGENCES.md`.

## Deferred debt

- Real Skyrim runtime verification remains deferred until the adapter runtime
  integration concept; it cannot be replaced by unit tests.
- Real cross-process IPC, Skyrim runtime verification, and process lifecycle remain in Concepts 03–04.

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
- Phase planning files: Concept 01 and Concept 02 completion, approved D1
  divergence, and handoff to Concept 03.

## Verification

- `git branch --show-current`: `feature/3-thin-native-adapter-and-private-ipc-continued`
- `host/PLAN.md` SHA-256: matches recorded fingerprint
- `dotnet build host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --no-restore`: passed
- `dotnet test host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --no-restore`: 426 passed
- `cmake --build adapter/build/windows-x64-debug --parallel`: passed
- `ctest --test-dir adapter/build/windows-x64-debug --output-on-failure`: 54 passed
- `dotnet format` verification: passed
- `clang-format --dry-run --Werror` verification: passed
- Native CMake's optional `applocal.ps1` dependency-copy helper reported that
  `dumpbin`, `llvm-objdump`, and `objdump` were unavailable; the build still
  returned success and the test executable ran all 54 tests.

## Handoff

Next concept: `03-native-adapter-core.md`
Blocked by: none

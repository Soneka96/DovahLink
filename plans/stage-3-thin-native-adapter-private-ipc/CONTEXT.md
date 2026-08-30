# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `4CFA216D1121044DCB046527AF7ED5A930300EC00934432709DDA575E2020584`
Phase: Stage 3 — Thin Native Adapter and Private IPC
Package: `plans/stage-3-thin-native-adapter-private-ipc/`
Status: active

## Active concept

- File: `02-host-ipc-channel.md`
- Status: pending
- Prerequisites: Stage 1, Stage 2, and Concept 01 complete; feature branch active
- Next action: Implement the C# private listener, adapter connection-generation handling, host-directed intent forwarding, and controlled resynchronization path.

## Completed concepts

- `01-private-ipc-contract.md`: complete. Host and native codecs, opaque event/sample intents, semantic validation, and golden vectors pass the focused checks.

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
- Live IPC I/O, queueing, and process lifecycle remain in Concepts 02–04.

## Changed files

- `host/DovahLink.Host/Adapter/Ipc/` and
  `host/DovahLink.Host.Tests/Adapter/Ipc/`: no-version C# IPC messages, codecs,
  validation, ownership handling, and tests.
- `host/DovahLink.Host/Constants.cs` and `host/DovahLink.Host/Enums.cs`:
  private IPC framing limits and message kinds.
- `adapter/ipc/` and `adapter/tests/ipc/`: matching C++ messages, codecs,
  validation, golden vectors, and structural tests.
- `adapter/CMakeLists.txt`, `adapter/CMakePresets.json`, and
  `adapter/vcpkg.json`: minimal independent native contract-test scaffold.
- Phase planning files: Concept 01 completion, approved D1 divergence, and
  handoff to Concept 02.

## Verification

- `git branch --show-current`: `feature/3-thin-native-adapter-and-private-ipc`
- `host/PLAN.md` SHA-256: matches recorded fingerprint
- `dotnet test host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --no-restore`: 335 passed
- `cmake --build adapter/build/windows-x64-debug --parallel`: passed
- `ctest --test-dir adapter/build/windows-x64-debug --output-on-failure`: 54 passed
- `dotnet format` verification: passed
- `clang-format --dry-run --Werror` verification: passed
- Native CMake's optional `applocal.ps1` dependency-copy helper reported that
  `dumpbin`, `llvm-objdump`, and `objdump` were unavailable; the build still
  returned success and the test executable ran all 54 tests.

## Handoff

Next concept: `02-host-ipc-channel.md`
Blocked by: none

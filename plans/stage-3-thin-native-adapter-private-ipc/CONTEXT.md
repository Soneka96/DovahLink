# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `43B9D53C4610462D86EE6C2F28C46B59FF4755389A1B96907AAA703AA0229324`
Phase: Stage 3 — Thin Native Adapter and Private IPC
Package: `plans/stage-3-thin-native-adapter-private-ipc/`
Status: active

## Active concept

- File: `04-process-lifecycle.md`
- Status: pending (revised before implementation; see "Decisions and approved
  deviations" below and D2 in `DIVERGENCES.md`)
- Prerequisites: Stage 1, Stage 2, and Concepts 01-03 complete; feature branch active
- Next action: Implement packaged host launch, endpoint rendezvous, mutual
  authentication, persistent supervision, and deterministic lifecycle handling
  per the revised Concept 04.

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
- Concept 04's Hello/HelloAck handshake is mutually authenticated (D2 in
  `DIVERGENCES.md`): the host proves it holds `peerProofToken` via an
  `HMAC-SHA256` `hostProof` bound to a fresh adapter-generated challenge, the
  correlation id, the adapter instance id, and `ownerLifetimeId`. The adapter
  authenticates only on the exact conjunction `accepted && matching
  correlationId && matching fresh challenge && matching ownerLifetimeId &&
  constantTimeEqual(hostProof, expectedProof)` -- `accepted = true` and a
  verifying `hostProof` are both required; neither substitutes for the other.
  Exact byte layout and a shared C++/C# known-answer test vector are recorded
  in `04-process-lifecycle.md`'s "Exact HMAC encoding".
- `ownerLifetimeId` (the owning Skyrim process's PID plus its
  `GetProcessTimes` creation timestamp, never PID alone) scopes the
  rendezvous file, the shutdown-request named event, and the handshake's
  lifetime check, so two Skyrim/adapter lifetimes running at the same time
  can never discover, adopt, or signal each other's host. It is a
  collision-avoidance value only, not a cryptographic ownership proof -- it
  is not secret, and a malicious same-Windows-user process forging or
  guessing it is explicitly out of scope, matching
  `ai/context/protocol/security.md`'s existing "Local-OS-user threat
  boundary". The rendezvous file is discovery only; adoption still requires
  passing the full mutual handshake above.
- The process-lifecycle supervisor is persistent for the adapter's whole
  process lifetime (watches for host loss and rediscovers/reconnects on a new
  dynamic port after a host restart), not a one-shot startup step. It never
  stops and restarts the existing `AdapterIpcConnection`/`AdapterIpcSession`
  pair to do this (that pair's `Stop()` permanently ends it and
  `AttachConnection` is called exactly once); it instead updates that
  connection's live target (port and presented proof) in place while it keeps
  running its own retry loop. A failed discovery round always self-schedules
  its own bounded retry rather than waiting on an external "connection lost"
  signal that a round which never connected in the first place could never
  produce.
- Shutdown follows one fixed order: mark the supervisor stopping (no new
  launch/reconnect may start), stop and join it (without releasing the
  launched host's process handle/Job Object), request graceful host shutdown
  and wait bounded for it to exit on its own, force-terminate via the Job
  Object only if that bound elapses, then stop and join the connection and
  destroy dependents. The Job Object is deliberately preserved through the
  graceful attempt so a premature handle-close can never turn a graceful
  request into an accidental forced kill. `DllMain`'s `DLL_PROCESS_DETACH`
  fires only the non-blocking shutdown *signal* -- never the bounded wait,
  never the forced-termination fallback, never a join. The full ordered
  sequence is a directly testable method with no confirmed safe production
  caller yet, since classic SKSE has no pre-exit main-thread hook -- recorded
  as deferred runtime debt below and in `04-process-lifecycle.md`'s
  "Shutdown ordering" and "Non-goals".

## Deferred debt

- Real Skyrim state mappings, outbound capture delivery, and accepted baseline
  resynchronization remain deferred until Stage 6; they cannot be replaced by
  foundation tests.
- Packaged host launch, endpoint rendezvous, and process lifecycle remain in
  Concept 04.
- The full ordered shutdown sequence (supervisor stop/join, graceful-shutdown
  wait, forced-termination fallback, connection stop/join, teardown) has no
  confirmed safe production caller: classic SKSE exposes no pre-exit
  main-thread hook, and `DllMain`'s `DLL_PROCESS_DETACH` may only fire the
  non-blocking shutdown signal. The orchestration method itself is built and
  directly unit-tested for its ordering contract; wiring it to a real caller
  is deferred until a safe pre-exit hook is confirmed (for example a future
  CommonLibSSE-NG addition).

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

# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `43B9D53C4610462D86EE6C2F28C46B59FF4755389A1B96907AAA703AA0229324`
Phase: Stage 3 — Thin Native Adapter and Private IPC
Package: `plans/stage-3-thin-native-adapter-private-ipc/`
Status: active

## Active concept

- File: `04-process-lifecycle.md`
- Status: in progress via `/step-build`, on branch
  `feature/3-thin-native-adapter-and-private-ipc-continued-3`. Steps 1-10 of
  16 complete and committed (one commit per step); steps 11-16 pending. See
  "Concept 04 step-build progress" below for the full step list and
  everything a fresh session needs to resume without re-deriving the design.
- Prerequisites: Stage 1, Stage 2, and Concepts 01-03 complete; feature branch active
- Next action: `/step-build` continuation, starting at Step 11 (adapter
  candidate handshake verifier). See below.

## Concept 04 step-build progress

Working via the `/step-build` skill: one step at a time, each step writes
production code then tests, gets an isolated fresh-eyes test-gap pass, gets
audited against this repo's own conventions, then hands off a commit message
and waits for the user to say "continue." All of steps 1-10 already committed
one commit per step; do not redo them. Re-read `04-process-lifecycle.md` in
full before continuing -- it is the corrected, authoritative design record
(mutual-auth predicate, lifetime scoping, persistent supervision, shutdown
ordering) and every remaining step must match it exactly.

### Steps 1-10 — complete

1. Wire contract: `challenge`/`ownerLifetimeId` added to `IpcHelloMessage`,
   `hostProof` added to `IpcHelloAckMessage`, `kLifetimeMismatch` reject
   reason -- both C++ (`adapter/ipc/`) and C# (`host/DovahLink.Host/Adapter/Ipc/`),
   golden vectors updated (D2 in `DIVERGENCES.md`).
2. `ComputeIpcHmacSha256` (BCrypt-backed HMAC-SHA256) in
   `adapter/ipc/adapter_ipc_hmac.hpp/.cpp`.
3. `DeriveOwnerLifetimeId`/`FormatOwnerLifetimeId`/`ParseOwnerLifetimeId` in
   new `adapter/process/adapter_owner_lifetime_id.hpp/.cpp` (C++ side).
4. `OwnerLifetimeId` readonly record struct (`ToBytes`/`FromBytes`/`Format`/
   `TryParse`) in `host/DovahLink.Host/Process/OwnerLifetimeId.cs` (C# side).
5. `AdapterIpcSession` (C++) now generates a fresh challenge per Hello via
   `GenerateIpcChallenge`, verifies the full mutual-auth conjunction on
   HelloAck (added `BuildHostProofMessage`/`ConstantTimeEqual` to
   `adapter_ipc_hmac.hpp`), and gates `HandleListenEvent`/`HandleReadSample`
   on `available_` both at enqueue time and again inside the deferred
   game-thread task (closes a real race: a second, rejecting HelloAck on the
   same connection generation could otherwise let an already-queued dispatch
   through). Constructor gained an `ownerLifetimeId` parameter; plugin
   composition (`dovahlink_adapter_plugin.cpp`) wires it via the real
   `process::DeriveOwnerLifetimeId()`.
6. `AdapterIpcSession.Handshake` (C#) validates `OwnerLifetimeId` and computes
   `HostProof` for an accepted handshake, via a private `BuildHostProofMessage`
   mirroring the C++ layout exactly. Constructor gained an optional
   `expectedOwnerLifetimeId` parameter (default matches `IpcHelloMessage`'s
   own all-zero default, so every pre-existing caller kept compiling
   unchanged).
7. `FileHostRendezvousPublisher`/`NamedEventHostShutdownSignal` in
   `host/DovahLink.Host/Process/HostRendezvous.cs`/`HostShutdownSignal.cs`.
   Both take their resolved path/name as an explicit constructor argument
   (never compute it internally) so tests never touch the real per-user
   `LOCALAPPDATA` or collide on named kernel objects. `Constants.cs` gained
   `RendezvousFilePath`/`ShutdownEventName`, both scoped by `OwnerLifetimeId`.
   Fixed a real bug here: `WaitAsync` must NOT pass its `CancellationToken` to
   `Task.Run` itself (that makes `Task.Run` cancel *scheduling* and throw
   `TaskCanceledException`) -- cancellation is observed only via the token's
   own `WaitHandle` raced inside `WaitAny`.
8. `Program.ComposeAndRunAsync` wires the real host stack (listener, session,
   rendezvous publish, stdout `PORT`/`PROOF` report, shutdown-signal race)
   for one Skyrim lifetime. `Main` parses `ownerLifetimeId` from
   `args[0]` via extracted, tested `ParseOwnerLifetimeIdArgument`.
9. `WinsockAdapterIpcSocket::SetPort` (port became atomic) and new
   `SettableAdapterIpcPeerProofProvider` (second impl of
   `IAdapterIpcPeerProofProvider`; the existing `FixedAdapterIpcPeerProofProvider`
   is untouched, still used by test fixtures) in `adapter/ipc/`.
10. `AdapterHostEndpoint` (port + proofToken) and
    `FileAdapterHostRendezvousReader`/`ResolveDefaultRendezvousFilePath` in
    new `adapter/process/adapter_host_endpoint.hpp` and
    `adapter_host_rendezvous_reader.hpp/.cpp`. Mirrors the host's rendezvous
    file format exactly; strips a trailing `\r` since the host writes
    Windows-style `\r\n` line endings but `std::getline` only splits on `\n`.

### Steps 11-16 — pending

11. **Adapter: candidate handshake verifier.** New
    `adapter/process/adapter_host_handshake_verifier.hpp/.cpp`:
    `IAdapterHostHandshakeVerifier`/`AdapterHostHandshakeVerifier`. Given a
    candidate `AdapterHostEndpoint` plus this adapter's instance id and
    `ownerLifetimeId`, performs ONE bounded, self-contained connect + Hello +
    HelloAck-read over a throwaway `WinsockAdapterIpcSocket` + `IpcFrameCodec`
    (does NOT touch the long-lived `AdapterIpcSession`/`AdapterIpcConnection`,
    since those are one-shot-only per Step 5's design notes): generates its
    own fresh challenge via `GenerateIpcChallenge()`, sends `IpcHelloMessage`,
    reads one frame within a bounded timeout, and verifies the exact same
    conjunction `AdapterIpcSession` verifies (reuse `BuildHostProofMessage` +
    `ComputeIpcHmacSha256` + `ConstantTimeEqual` from `adapter_ipc_hmac.hpp`
    -- do not duplicate that logic). Returns bool (or an
    `std::optional<AdapterHostEndpoint>` echoing the verified candidate).
    Tests: accepted; forged/wrong/missing hostProof; wrong `ownerLifetimeId`;
    unreachable port; timed-out peer (mirror the fresh-eyes coverage already
    proven for `AdapterIpcSession` in Step 5 and `adapter_ipc_hmac_test.cpp`
    in Step 2, applied to this one-shot verifier).

12. **Adapter: hidden process launcher.** New
    `adapter/process/adapter_host_process_launcher.hpp/.cpp`:
    `IAdapterHostProcessLauncher`/`Win32AdapterHostProcessLauncher`. Launches
    the packaged host hidden via `CreateProcessW` (structured args -- no
    shell text -- passing the adapter's own `FormatOwnerLifetimeId(...)` hex
    as `argv[1]`, matching `Program.ParseOwnerLifetimeIdArgument`'s
    expectation; `STARTF_USESHOWWINDOW`/`SW_HIDE`; redirected stdout pipe),
    parses the bounded `PORT`/`PROOF` stdout lines with a timeout (mirror
    `FileAdapterHostRendezvousReader`'s parsing, or better, extract a shared
    parse helper if the formats are identical enough -- check before
    duplicating), assigns the child to a Job Object with
    `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Also add a separate, small
    `IAdapterHostShutdownRequester`/`WindowsEventAdapterHostShutdownRequester`
    (`OpenEvent`+`SetEvent` on `Local\DovahLink.Host.Shutdown.<hex>` --
    matching `Constants.ShutdownEventName` on the host side exactly --
    safe to call from `DllMain`, no-op if nothing is listening) and a
    separate `AwaitExitOrTerminate(timeout)` method (waits bounded on the
    retained process handle, force-terminates via the Job Object only if the
    bound elapses -- **never called from `DllMain`**, only from the Step 14
    orchestration method). New `adapter/process/adapter_host_constants.hpp`
    for the host exe's relative path (packaging is a non-goal; document the
    assumed layout), retry bounds, and timeouts. Tests: hidden launch,
    stdout parsing, bounded timeout on a silent process, Job Object
    kill-on-close, `RequestShutdown()` with nothing listening (mirror
    `HarnessProcess`'s `cmd.exe`-echo pattern in
    `integration/DovahLinkValidationClient.Tests/HarnessProcess.cs` for a
    controllable stand-in process).

13. **Adapter: persistent host supervisor.** New
    `adapter/process/adapter_host_supervisor.hpp/.cpp`:
    `IAdapterHostSupervisor`/`AdapterHostSupervisor`. Implements the EXACT
    state machine recorded in `04-process-lifecycle.md`'s "Discovery-round
    retry state machine": on `Start()` and after every subsequent
    "connection lost" signal, run one bounded round (try
    `IAdapterHostRendezvousReader::TryRead()` then verify via
    `IAdapterHostHandshakeVerifier`; on failure, try
    `IAdapterHostProcessLauncher::Launch()` then verify that candidate too);
    on success, reconfigure the EXISTING long-lived socket/proof-provider via
    `SetPort`/`SetToken` (added in Step 9) -- **never** stop/restart the
    `AdapterIpcConnection`/`AdapterIpcSession` pair. A round that exhausts
    its bounded attempts must self-schedule its own bounded retry -- never
    wait only on an external "disconnected" signal that a round which never
    connected could never produce. `RequestStop()` marks stopping first (no
    new round begins), interrupts an in-flight bounded wait, then joins its
    background thread. The plugin composition's `onDisconnected` callback
    must also notify this supervisor so it knows to run another round.
    Tests: initial adoption success; candidate rejected then fresh launch
    succeeds; host crash + restart on a new dynamic port reconnects live
    (the actual point of this whole step); `RequestStop()` during an
    in-flight round starts no further round and no relaunch; repeated
    failures with later recovery; shutdown racing launch/reconnect.

14. **Adapter: ordered shutdown, plugin composition, `DllMain`.** Implement
    the exact 5-step sequence from `04-process-lifecycle.md`'s "Shutdown
    ordering" (mark supervisor stopping -> stop/join supervisor without
    releasing the Job Object -> request graceful shutdown + bounded
    `AwaitExitOrTerminate` -> force-terminate only if that bound elapses ->
    stop/join the connection and destroy dependents) as one directly
    testable orchestration method, separate from the signal-only call.
    Rewire `dovahlink_adapter_plugin.cpp`: construct the supervisor with real
    collaborators (reader, launcher, verifier, the settable socket/proof
    provider from Step 9), start it on `kDataLoaded` instead of calling
    `connection.Start()` directly, remove `kProvisionalHostIpcPort`/
    `kProvisionalPeerProofToken`. Add `DllMain`/`DLL_PROCESS_DETACH` calling
    **only** the non-blocking `RequestShutdown()` signal -- never the
    orchestration method, never a join, never `AwaitExitOrTerminate`. Update
    `dovahlink_adapter_plugin_test.cpp`'s structural assertions for the new
    composition shape; add a structural check that `DllMain` calls only the
    signal path. Record the "no confirmed safe production caller for the
    full sequence" gap explicitly in a comment/doc, matching
    `04-process-lifecycle.md`'s "Non-goals" -- do not claim `DllMain` performs
    the complete shutdown.

15. **CMakeLists.txt wiring.** Add every new `adapter/process/*.cpp` and any
    new `adapter/ipc/*.cpp` source from steps 11-14 to `dovahlink_adapter_core`,
    and their test files to `dovahlink_adapter_tests` (same pattern already
    used incrementally in steps 2-3, 9-10's edits to `adapter/CMakeLists.txt`
    -- this step is really just "catch anything not already wired inline").

16. **Docs close-out.** Update this file's "Active concept" status to
    complete, add final verification numbers (`dotnet test` and `ctest`
    pass counts), and update `PLAN.md`'s traceability/phase-completion gate
    notes for Stage 3.

### Things the next session needs that aren't obvious from the code alone

- **Build commands.** Plain `Bash` cannot build the C++ side (no MSVC
  `INCLUDE` env) -- CMake configure/build/test for the adapter must run
  through `PowerShell`, sourcing the VS Developer Shell first, e.g.:
  ```
  Set-Location "C:\Projects\DovahLink"
  & "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\Launch-VsDevShell.ps1" -Arch amd64 -HostArch amd64 *> $null
  Set-Location "C:\Projects\DovahLink"
  cmake --build "C:\Projects\DovahLink\adapter\build\windows-x64-debug" --parallel
  ctest --test-dir "C:\Projects\DovahLink\adapter\build\windows-x64-debug" --output-on-failure
  ```
  Both commands must be in the SAME PowerShell call (environment does not
  persist across separate tool invocations). The host C# side builds fine
  directly in `Bash` via `dotnet test host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --no-restore`.
- **Branch.** Must be on `feature/3-thin-native-adapter-and-private-ipc-continued-3`,
  not `main` -- this session drifted back to `main` once mid-build already;
  re-verify with `git branch --show-current` before editing anything.
- **Formatting diffs are expected and benign.** Files routinely show as
  "changed on disk since last read" between turns (clang-format/dotnet
  format auto-formatting) -- these are whitespace-only; verify with a quick
  diff read if unsure, but do not manually reformat.
- **The `/step-build` cadence**: each step = prod code, then tests, then a
  foreground `Agent` (subagent_type `Explore`, model `haiku`) fresh-eyes
  test-gap pass with a self-contained prompt (the agent has no memory of
  this conversation), then apply only the gaps that are real/non-redundant
  (many suggested gaps turn out to test scenarios already covered by a
  different input, or scenarios the BCL/OS/an earlier layer already rules
  out -- judge each one, don't apply blindly), then a convention audit (grep
  for stray "04-process-lifecycle.md"/"DIVERGENCES.md" citations in new code
  comments -- common.md forbids citing planning docs as behavioral authority
  in code, and this mistake recurred every single step), then build+test
  both sides, then hand off a commit message and STOP for "continue". Do
  not batch multiple steps' code before running tests.
- **Do not re-litigate the design.** `04-process-lifecycle.md`,
  `DIVERGENCES.md`'s D2, and this file's "Decisions and approved deviations"
  above are the maintainer-approved, already-corrected spec (it went through
  two rounds of maintainer review before implementation started). Steps
  11-14 have real design freedom in *how* to structure the code but must
  match every documented invariant exactly -- especially the mutual-auth
  predicate, the never-secret framing of `ownerLifetimeId`, the
  never-restart-the-connection rule, and the DllMain-signal-only rule.

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

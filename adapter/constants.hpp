#pragma once

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <filesystem>

namespace dovahlink::adapter::capture {

//  ---- Capture ----

///  The bounded capacity of the adapter's capture handoff queue. A game-thread
///  callback that would exceed this capacity is rejected rather than waited
///  for, per `ai/context/adapter/architecture.md`'s bounded, non-blocking
///  handoff requirement.
inline constexpr std::size_t kMaxAdapterCaptureQueueItems = 64;

///  The largest captured value any current capture unit produces (the
///  12-byte coherent vitals sample), sizing `CapturedPayload`'s fixed buffer
///  so no capture ever needs a heap allocation to hold its own value. This is
///  a requirement-driven bound, not an intentional architectural ceiling:
///  raising it is fine once a real capture unit needs more than 12 bytes, but
///  it must not be raised speculatively ahead of one.
inline constexpr std::size_t kMaxCapturedPayloadBytes = 12;

///  The number of immediate, non-blocking lock attempts
///  `AdapterCaptureHandoffQueue::TryEnqueue` makes before treating an item as
///  rejected; it never voluntarily yields, sleeps, or waits between them. The
///  worker thread's own critical section is intentionally extremely short --
///  it holds the same mutex only to remove one item from the ring buffer --
///  so a handful of immediate retries can absorb ordinary, brief concurrent
///  contention with it, for example three baseline samples enqueued back to
///  back during resynchronization, without the calling game-thread callback
///  ever waiting for the lock or surrendering its scheduler timeslice.
///  Admission stays deliberately bounded and non-blocking, so a
///  contention-only rejection remains possible if the worker happens to be
///  preempted mid-critical-section; that tradeoff is preferred over risking
///  scheduler-dependent latency on this Skyrim producer path. A genuinely
///  full or stopped queue still rejects immediately, on the very first
///  attempt that acquires the mutex. This is the production default;
///  `AdapterCaptureHandoffQueue`'s constructor accepts an override for a
///  caller that is provably never the real Skyrim game thread -- see its
///  own parameter doc for why a real-process CTest fixture is one such
///  caller.
inline constexpr int kCaptureQueueEnqueueLockAttempts = 4;

} //  namespace dovahlink::adapter::capture

namespace dovahlink::adapter::ipc {

//  ---- IPC ----

//  ---- Framing ----

///  The fixed byte length of an IPC frame header (kind and correlation id).
inline constexpr std::size_t kIpcFrameHeaderBytes = 9;

//  ---- Limits ----

///  The maximum total byte length (header plus payload) of one private IPC
///  frame, sized for this channel's current small control-only messages.
///  Revising it to carry a larger payload requires the same documented
///  approval `ai/context/protocol/security.md`'s own limits require.
inline constexpr std::size_t kMaxIpcFrameBytes = 65536;

///  The maximum byte length of an `IpcHelloMessage` peer-ownership proof token.
inline constexpr std::size_t kMaxIpcPeerProofTokenBytes = 64;

///  The maximum number of persistent event keys in a resynchronization plan.
inline constexpr std::size_t kMaxResynchronizationEventKeys = 16;

///  The maximum number of baseline sample tokens in a resynchronization plan.
inline constexpr std::size_t kMaxResynchronizationSampleTokens = 32;

///  The bounded capacity of `AdapterIpcConnection`'s own outbound queue (see
///  `IAdapterIpcConnection::TrySend`).
inline constexpr std::size_t kMaxIpcQueuedMessages = 256;

///  The maximum inbound private IPC message rate `AdapterIpcConnection`
///  enforces per connected peer (see its `TryAcceptInboundMessage`).
inline constexpr std::size_t kMaxIpcMessagesPerSecond = 200;

///  The maximum number of deferred game-thread dispatches `AdapterIpcSession`
///  admits at once (resynchronization, listen-event, and read-sample
///  requests marshaled onto the game thread but not yet run). Bounds memory
///  growth when the game thread is paused or overloaded; a request beyond
///  this capacity is rejected without blocking. Matches
///  `kMaxAdapterCaptureQueueItems`, since admitting more game-thread work than
///  the downstream capture queue can absorb has no benefit.
inline constexpr std::size_t kMaxPendingGameThreadDispatches = 64;

//  ---- Connection ----

///  The absolute bound on one long-lived connection's pre-authentication
///  Hello/HelloAck establishment phase. This is intentionally separate from
///  post-authentication liveness and matches the candidate verifier's
///  provisional two-second handshake policy.
inline constexpr std::chrono::milliseconds kAdapterIpcEstablishmentTimeout{
    2000};

//  ---- Authentication ----

///  The byte length of the adapter-generated random challenge carried in
///  `IpcHelloMessage`, and of the host's resulting HMAC-SHA256 `hostProof` in
///  `IpcHelloAckMessage`.
inline constexpr std::size_t kIpcChallengeBytes = 32;

///  The byte length of the owning Skyrim process's lifetime identity
///  (`ownerLifetimeId`) carried in `IpcHelloMessage`: a 4-byte process id and
///  an 8-byte process creation timestamp.
inline constexpr std::size_t kIpcOwnerLifetimeIdBytes = 12;

///  The byte length of `IpcHelloAckMessage`'s HMAC-SHA256 `hostProof`. Equal
///  to `kIpcChallengeBytes` because both are full, untruncated HMAC-SHA256
///  outputs, but named separately since they serve different roles on the
///  wire.
inline constexpr std::size_t kIpcHostProofBytes = 32;

///  The fixed byte length of the message `hostProof` is computed over:
///  `challenge (kIpcChallengeBytes) || correlationId (8) ||
///  adapterInstanceId (16) || ownerLifetimeId (kIpcOwnerLifetimeIdBytes)`.
inline constexpr std::size_t kIpcHostProofMessageBytes =
    kIpcChallengeBytes + 8 + 16 + kIpcOwnerLifetimeIdBytes;

//  ---- Pairing ----

///  The number of ASCII decimal digits in an `IpcPairingDisplayMessage`'s
///  pairing code, matching the host's own generated pairing challenge code
///  length.
inline constexpr std::size_t kPairingChallengeCodeDigits = 6;

//  ---- Trust administration ----

///  The number of ASCII decimal digits in an `IpcTrustAdminRequestMessage`'s
///  `shortId` argument, matching the host's own generated short id length.
inline constexpr std::size_t kPairingShortIdDigits = 5;

///  The number of ASCII decimal digits in an `IpcTrustAdminRequestMessage`'s
///  `confirmationCode` argument, matching the host's own generated Factory
///  Reset confirmation code length.
inline constexpr std::size_t kFactoryResetChallengeCodeDigits = 6;

///  The maximum UTF-8 byte length of an `IpcTrustAdminResultMessage`'s
///  `resultText`, matching the host's own bound on formatted result text.
inline constexpr std::size_t kMaxIpcTrustAdminResultTextBytes = 4096;

///  The absolute bound `AdapterIpcSession::SendTrustAdminRequest` waits for
///  its correlated `IpcTrustAdminResultMessage`, covering the host's own
///  trust-service dispatch and persistence write. Generous relative to
///  `kAdapterIpcEstablishmentTimeout` because a trust-administration command
///  is a rare, explicitly user-triggered console action, not a per-frame or
///  per-connection-lifecycle operation, so a slower bound here has no effect
///  on ordinary connection throughput or Skyrim's own responsiveness.
inline constexpr std::chrono::milliseconds kTrustAdminRequestTimeout{5000};

///  The maximum number of trust-admin requests `AdapterIpcSession` admits at
///  once, counted from admission until the request's own timeout worker (or,
///  for an immediately-failed send, the original caller) has actually
///  finished handling it -- not merely until a correlated result, a timeout,
///  or a connection close resolves it. Strictly bounds both memory growth in
///  `pendingTrustAdminResults_` and the number of concurrently outstanding
///  timeout worker threads a rapid sequence of Papyrus-originated commands
///  could otherwise spawn: a request beyond this capacity is rejected without
///  being sent or spawning a worker, and an already-resolved request does not
///  free its slot until the thread that owns it has actually finished. Sized
///  well above any plausible number of concurrent manual console commands,
///  since this is a rare control-path API rather than a per-frame or
///  per-connection-lifecycle operation.
inline constexpr std::size_t kMaxPendingTrustAdminRequests = 16;

} //  namespace dovahlink::adapter::ipc

namespace dovahlink::adapter::process {

//  ---- Process launch ----

///  The packaged host executable's path relative to the adapter plugin's own
///  directory, matching the layout `tooling/adapter_host_packager.py`
///  produces (`DovahLink.Host/DovahLink.Host.exe` beside the adapter plugin
///  DLL). Combined with that directory by the plugin composition root, which
///  is the only place able to resolve its own module path.
inline const std::filesystem::path kAdapterHostExecutableRelativePath =
    "DovahLink.Host/DovahLink.Host.exe";

///  The default bound `Win32AdapterHostProcessLauncher` waits for a newly
///  launched host process to report its endpoint over its redirected
///  stdout, before treating the launch as failed.
inline constexpr std::chrono::milliseconds kDefaultAdapterHostLaunchTimeout{
    5000};

///  How often `Win32AdapterHostProcessLauncher` polls a launched process's
///  redirected stdout pipe for new bytes. Anonymous pipes do not support
///  overlapped (asynchronous) I/O, so a short poll is the bounded alternative
///  to a blocking read with no timeout.
inline constexpr std::chrono::milliseconds kAdapterHostLaunchStdoutPollInterval{
    20};

///  The maximum bytes `Win32AdapterHostProcessLauncher` buffers from a
///  launched process's stdout while waiting for its three-line endpoint
///  report, so a launched process that never produces a newline cannot grow
///  that buffer unbounded.
inline constexpr std::size_t kMaxAdapterHostEndpointReportBytes = 1024;

///  The maximum bytes `FileAdapterHostRendezvousReader` accepts for any one
///  line of a three-line endpoint report before rejecting it. This is a
///  bounded parser buffer, not an expansion of the protocol's proof-token
///  limit.
inline constexpr std::size_t kMaxAdapterHostRendezvousLineBytes = 1024;

///  The bound `Win32AdapterHostProcessLauncher::AwaitExitOrTerminate` waits
///  for the operating system to finish tearing down a process after
///  force-termination is requested, before returning. Force-termination
///  itself is effectively immediate; this only bounds the brief window
///  between requesting it and the OS actually reclaiming the process.
inline constexpr std::chrono::milliseconds kAdapterHostForceTerminateGraceWait{
    5000};

//  ---- Supervision ----

///  The default bound `AdapterHostSupervisor` waits before retrying a
///  discovery round that produced no candidate, so a persistently unreachable
///  host is retried indefinitely without spinning.
inline constexpr std::chrono::milliseconds
    kDefaultAdapterHostSupervisorFailedRoundBackoff{1000};

//  ---- Shutdown orchestration ----

///  The default bound `AdapterShutdownOrchestrator` waits for a launched
///  host to exit gracefully after signaling it, before force-terminating it
///  as the deliberate fallback.
inline constexpr std::chrono::milliseconds
    kDefaultAdapterGracefulShutdownWaitBound{3000};

} //  namespace dovahlink::adapter::process

namespace dovahlink::adapter::runtime {

//  ---- Game behavior compatibility ----

///  Path to the optional runtime-compatibility INI file, relative to the
///  Skyrim installation's working directory.
inline constexpr const char* kAdapterGameBehaviorConfigPath =
    "Data/SKSE/Plugins/DovahLinkAdapter.ini";

///  INI section `DovahLink` from which
///  `adapter_game_behavior_config_file_reader.cpp` reads compatibility keys.
inline constexpr const char* kAdapterGameBehaviorConfigSection = "DovahLink";

///  INI key controlling `AdapterGameBehaviorConfig::alwaysActive`.
inline constexpr const char* kAdapterAlwaysActiveKey = "bAlwaysActive";

///  INI key controlling `AdapterGameBehaviorConfig::achievementCompat`.
inline constexpr const char* kAdapterAchievementCompatKey =
    "bAchievementCompat";

} //  namespace dovahlink::adapter::runtime

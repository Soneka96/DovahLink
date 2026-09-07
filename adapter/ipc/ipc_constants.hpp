#pragma once

#include <chrono>
#include <cstddef>
#include <cstdint>

namespace dovahlink::adapter::ipc {

//  ---- Framing ----

///  The fixed byte length of an IPC frame header (kind and correlation id).
inline constexpr std::size_t kIpcFrameHeaderBytes = 9;

//  ---- Limits ----

///  The maximum total byte length (header plus payload) of one private IPC
///  frame. Approved as a provisional value for this concept's small
///  control-only messages; a later concept that needs to carry a larger payload
///  over this channel may revise it with the same documented approval
///  `ai/context/protocol/security.md`'s own limits require.
inline constexpr std::size_t kMaxIpcFrameBytes = 65536;

///  The maximum byte length of an `IpcHelloMessage` peer-ownership proof token.
inline constexpr std::size_t kMaxIpcPeerProofTokenBytes = 64;

///  The bounded capacity later concepts must enforce for a private IPC
///  send/receive queue. Not itself enforced by this contract's codec.
inline constexpr std::size_t kMaxIpcQueuedMessages = 256;

///  The maximum inbound private IPC message rate later concepts must enforce,
///  per connected peer. Not itself enforced by this contract's codec.
inline constexpr std::size_t kMaxIpcMessagesPerSecond = 200;

///  The maximum number of deferred game-thread dispatches `AdapterIpcSession`
///  admits at once (resynchronization, listen-event, and read-sample
///  requests marshaled onto the game thread but not yet run). Bounds memory
///  growth when the game thread is paused or overloaded; a request beyond
///  this capacity is rejected without blocking. Matches
///  `kMaxAdapterCaptureQueueItems`, since admitting more game-thread work than
///  the downstream capture queue can absorb has no benefit.
inline constexpr std::size_t kMaxPendingGameThreadDispatches = 64;

///  The maximum number of not-yet-consumed `IpcCancelMessage` correlation ids
///  `AdapterIpcSession` remembers at once. Bounds memory growth from
///  cancellations whose target request already finished, was dropped by a
///  disconnect, or never arrived; the oldest entry is evicted to admit a new
///  one past this capacity.
///
///  Deliberately equal to `kMaxPendingGameThreadDispatches`, not an
///  independent value: every cancellable dispatch is one of that bound's own
///  admitted entries, so at most `kMaxPendingGameThreadDispatches` distinct
///  correlation ids can ever be genuinely outstanding and worth cancelling at
///  once. Matching this capacity to that bound means the oldest-entry
///  eviction above can never discard a tombstone for a dispatch that is
///  still actually queued -- filling this deque to capacity from genuinely
///  outstanding cancellations alone is only possible when every admitted
///  dispatch has already been cancelled, at which point evicting the oldest
///  one is harmless since a newer cancellation is arriving to replace it.
///  Eviction remains reachable only from cancellations whose target already
///  finished, was dropped, or never arrived -- messages this session's own
///  mutually authenticated Host peer controls the volume of, the same trust
///  boundary every other private-channel bound in this file already relies
///  on. Keep these two values equal if either one is ever revised.
inline constexpr std::size_t kMaxPendingIpcCancellations =
    kMaxPendingGameThreadDispatches;

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

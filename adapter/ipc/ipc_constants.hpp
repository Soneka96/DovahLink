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

//  ---- Connection ----

///  The fixed delay `AdapterIpcConnection` waits before retrying a failed
///  connect attempt or reconnecting after a disconnect, per
///  `ai/context/host/architecture.md`'s "adapter reconnect is bounded and
///  performed outside game-thread work". A fixed delay, not exponential
///  backoff, matching the host listener's own `AdapterIpcAcceptRetryDelay`
///  precedent. Approved as a provisional value; a later concept may revise it
///  with the same documented approval this file's other limits require.
inline constexpr std::chrono::milliseconds kAdapterIpcReconnectDelay{200};

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

} //  namespace dovahlink::adapter::ipc

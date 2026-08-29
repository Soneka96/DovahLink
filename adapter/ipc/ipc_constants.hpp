#pragma once

#include <cstddef>
#include <cstdint>

namespace dovahlink::adapter::ipc {

//  ---- Framing ----

///  The private host-to-adapter IPC protocol version this build supports. A
///  frame declaring any other version fails closed rather than being
///  interpreted.
inline constexpr std::uint8_t kSupportedIpcProtocolVersion = 1;

///  The fixed byte length of an IPC frame header (protocol version, kind, and
///  correlation id).
inline constexpr std::size_t kIpcFrameHeaderBytes = 10;

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

} //  namespace dovahlink::adapter::ipc

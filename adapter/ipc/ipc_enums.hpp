#pragma once

#include <cstdint>

namespace dovahlink::adapter::ipc {

//  ---- Framing ----

///  The kind of message carried by one private host-to-adapter IPC frame.
enum class IpcMessageKind : std::uint8_t {
  ///  Sent by the connecting adapter to negotiate the channel. See
  ///  `IpcHelloMessage`.
  kHello = 1,
  ///  Sent by the host to conclude negotiation. See `IpcHelloAckMessage`.
  kHelloAck = 2,
  ///  Sent by the host to request a fresh baseline. See
  ///  `IpcResynchronizeRequestMessage`.
  kResynchronizeRequest = 3,
  ///  Sent by the adapter in response to a resynchronization request. See
  ///  `IpcResynchronizeResultMessage`.
  kResynchronizeResult = 4,
  ///  Sent by either side to announce a deterministic close. See
  ///  `IpcCloseMessage`.
  kClose = 5,
  ///  Sent by either side to reject a decodable-but-invalid message. See
  ///  `IpcRejectMessage`.
  kReject = 6,
  ///  Sent by either side to cancel a previously sent request. See
  ///  `IpcCancelMessage`.
  kCancel = 7,
  ///  Sent by the host to ask the adapter to register one opaque event key.
  kListenEvent = 8,
  ///  Sent by the host to ask the adapter to perform one opaque sample token.
  kReadSample = 9,
};

///  Why a private IPC channel is being closed.
enum class IpcCloseReason : std::uint8_t {
  ///  An ordinary, non-error close.
  kNormal = 0,
  ///  The sending process is shutting down.
  kShutdown = 1,
  ///  The sender is closing because of an unrecoverable error.
  kError = 2,
};

///  Why the private IPC codec fail-closed rejected a frame it could still
///  safely decode.
enum class IpcRejectReason : std::uint8_t {
  ///  The frame's declared length is impossible or exceeds the configured
  ///  limit.
  kMalformedFrameLength = 0,
  ///  The frame's message kind is not a recognized value.
  kUnknownMessageKind = 1,
  ///  A peer-ownership proof in the payload is structurally invalid.
  kInvalidIdentity = 2,
  ///  The payload bytes do not match the fixed or declared layout for the
  ///  frame's kind.
  kMalformedPayload = 3,
};

///  Why the host rejected an `IpcHelloMessage` negotiation.
enum class IpcHelloRejectReason : std::uint8_t {
  ///  Negotiation was not rejected; used only when the hello was accepted.
  kNone = 0,
  ///  The peer-ownership proof did not match the expected value.
  kInvalidProof = 1,
  ///  The hello payload was structurally invalid.
  kMalformed = 2,
};

} //  namespace dovahlink::adapter::ipc

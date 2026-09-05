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
  ///  Sent by the host to ask the adapter to present a pairing code. See
  ///  `IpcPairingDisplayMessage`.
  kPairingDisplay = 10,
  ///  Sent by the adapter in response to a pairing display request. See
  ///  `IpcPairingDisplayAckMessage`.
  kPairingDisplayAck = 11,
  ///  Sent by the host to present a no-code attempts-exhausted notification.
  ///  See `IpcPairingAttemptsExhaustedMessage`.
  kPairingAttemptsExhausted = 12,
  ///  Sent by the adapter to forward a Papyrus-originated trust-administration
  ///  command. See `IpcTrustAdminRequestMessage`.
  kTrustAdminRequest = 13,
  ///  Sent by the host in response to a trust-administration request. See
  ///  `IpcTrustAdminResultMessage`.
  kTrustAdminResult = 14,
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
  ///  The Hello's `ownerLifetimeId` did not match the value this host
  ///  process was launched with.
  kLifetimeMismatch = 3,
};

///  Which Skyrim-facing pairing-code display intent an
///  `IpcPairingDisplayMessage` carries.
enum class PairingDisplayMode : std::uint8_t {
  ///  The first display of a freshly generated code.
  kInitial = 0,
  ///  A client-requested manual redisplay of the still-active code.
  kManualRedisplay = 1,
  ///  A best-effort redisplay after a wrong evaluated code, shown with
  ///  incorrect-attempt presentation.
  kWrongCodeRedisplay = 2,
};

///  The closed set of adapter-originated trust-administration commands an
///  `IpcTrustAdminRequestMessage` may carry, per
///  `ai/context/protocol/security.md`'s "Trust administration surface".
enum class TrustAdminOperation : std::uint8_t {
  ///  Returns the canonical trust-administration command help. No argument.
  kHelp = 0,
  ///  Lists known devices in the requested scope.
  kList = 1,
  ///  Revokes a trusted device by short id.
  kRevoke = 2,
  ///  Blocks a known device by short id.
  kBlock = 3,
  ///  Unblocks a blocked device by short id.
  kUnblock = 4,
  ///  Forgets an eligible device by short id.
  kForget = 5,
  ///  Resets every trusted device back to unpaired. No argument.
  kResetTrust = 6,
  ///  Starts a Factory Reset confirmation challenge. No argument.
  kReset = 7,
  ///  Confirms a Factory Reset challenge with its six-digit code.
  kConfirmReset = 8,
};

///  The device scope an `IpcTrustAdminRequestMessage`'s
///  `TrustAdminOperation::kList` operation requests.
enum class TrustAdminListScope : std::uint8_t {
  ///  Every known device.
  kAll = 0,
  ///  Only currently trusted devices.
  kTrust = 1,
  ///  Only currently blocked devices.
  kBlock = 2,
};

} //  namespace dovahlink::adapter::ipc

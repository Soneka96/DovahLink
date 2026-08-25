#pragma once

#include <string_view>

namespace dovahlink::protocol {

//  ---- Message types ----

///  Identifiers for the registered protocol message types.
namespace message_type {
inline constexpr std::string_view kHello = "hello";
inline constexpr std::string_view kHelloAck = "hello_ack";
inline constexpr std::string_view kCapabilities = "capabilities";
inline constexpr std::string_view kSubscribe = "subscribe";
inline constexpr std::string_view kSubscriptionAck = "subscription_ack";
inline constexpr std::string_view kSnapshotRequest = "snapshot_request";
inline constexpr std::string_view kStateSnapshot = "state_snapshot";
inline constexpr std::string_view kStateEvent = "state_event";
inline constexpr std::string_view kError = "error";
inline constexpr std::string_view kPing = "ping";
inline constexpr std::string_view kPong = "pong";
inline constexpr std::string_view kPairingRequest = "pairing_request";
inline constexpr std::string_view kPairingStatus = "pairing_status";
inline constexpr std::string_view kPairingConfirm = "pairing_confirm";
inline constexpr std::string_view kPairingAck = "pairing_ack";
inline constexpr std::string_view kPairingOutcome = "pairing_outcome";
inline constexpr std::string_view kPairingRenotify = "pairing_renotify";
inline constexpr std::string_view kPairingCancel = "pairing_cancel";
inline constexpr std::string_view kRenameRequest = "rename_request";
inline constexpr std::string_view kRenameOutcome = "rename_outcome";
inline constexpr std::string_view kSessionInvalidated = "session_invalidated";
} //  namespace message_type

//  ---- State areas ----

///  Identifiers for registered state areas. No state area is currently
///  registered; the previous `character` aggregate was retired without a
///  replacement (protocol/schema/README.md's "Registered state areas"). A future
///  phase adds identifiers here when it registers one.
namespace state_area {} //  namespace state_area

} //  namespace dovahlink::protocol

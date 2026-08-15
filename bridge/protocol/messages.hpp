#pragma once

#include "protocol/decode_error.hpp"
#include "protocol/envelope.hpp"

#include <boost/json/object.hpp>
#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dovahlink::protocol {

// Payload codecs validate structural shape and types only. Stateful rules such
// as revision sequencing and stale/gap detection belong to the application
// layer. occurredAt is required to be a non-empty string but is not parsed as
// an ordering source.

/// Identifiers for the registered protocol message types.
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
}  // namespace message_type

/// Identifiers for registered state areas.
namespace state_area {
inline constexpr std::string_view kCharacter = "character";
}  // namespace state_area

/// Reports a message-payload decoding failure.
using MessageError = DecodeError;

/// Decoded client authentication and identity data.
struct HelloPayload {
    /// Endpoint role declared by the client.
    std::string endpoint;
    /// Authentication method identifier.
    std::string authMethod;
    /// Authentication token supplied by the client.
    std::string authToken;
    /// Identifier of the logical client establishing this connection. Shape-only
    /// here: a required non-empty string is enforced by the payload decoder;
    /// see `messages.hpp`'s "codecs validate shape, application validates rules"
    /// split for how the application layer validates it against an existing
    /// session.
    std::string clientId;
};

/// Decodes a client hello payload and validates its supported authentication form.
std::expected<HelloPayload, MessageError> DecodeHelloPayload(
    const boost::json::object& payload);

/// Encoded response to a successful handshake, exposing the bridge's own
/// compatibility identity to the client.
struct HelloAckPayload {
    /// The DovahLink Bridge/mod release version (matching `bridge/vcpkg.json`'s
    /// `version-string`), for the client to evaluate against its own declared
    /// supported range. See `ai/context/protocol/compatibility.md`.
    std::string bridgeVersion;
    /// Kind of client identity established by this handshake. Always
    /// `"unpaired"` in the current phase; a future pairing phase adds
    /// `"paired"` without changing this shape.
    std::string clientIdentityKind;
};

/// Decodes a hello acknowledgment payload.
std::expected<HelloAckPayload, MessageError> DecodeHelloAckPayload(
    const boost::json::object& payload);

/// Encodes a hello acknowledgment payload.
boost::json::object EncodeHelloAckPayload(const HelloAckPayload& payload);

/// Advertised capability identifier and version.
struct Capability {
    /// Stable capability identifier.
    std::string id;
    /// Capability schema or implementation version.
    std::int64_t version = 0;
};

/// Collection of capabilities advertised by an endpoint.
struct CapabilitiesPayload {
    /// Capabilities included in the message.
    std::vector<Capability> capabilities;
};

/// Decodes a capabilities payload.
std::expected<CapabilitiesPayload, MessageError> DecodeCapabilitiesPayload(
    const boost::json::object& payload);

/// Encodes a capabilities payload.
boost::json::object EncodeCapabilitiesPayload(const CapabilitiesPayload& payload);

/// Client request for state-area subscriptions.
struct SubscribePayload {
    /// State areas requested by the client.
    std::vector<std::string> stateAreas;
};

/// Decodes a subscription request payload.
std::expected<SubscribePayload, MessageError> DecodeSubscribePayload(
    const boost::json::object& payload);

/// Bridge response listing accepted and rejected subscription areas.
struct SubscriptionAckPayload {
    /// State areas accepted by the bridge.
    std::vector<std::string> acceptedStateAreas;
    /// State areas rejected by the bridge.
    std::vector<std::string> rejectedStateAreas;
};

/// Decodes a subscription acknowledgment payload.
std::expected<SubscriptionAckPayload, MessageError> DecodeSubscriptionAckPayload(
    const boost::json::object& payload);

/// Encodes a subscription acknowledgment payload.
boost::json::object EncodeSubscriptionAckPayload(const SubscriptionAckPayload& payload);

/// Client request for a state snapshot and optional known revision.
struct SnapshotRequestPayload {
    /// State area whose snapshot is requested.
    std::string stateArea;
    /// Client's latest known revision, when available.
    std::optional<std::int64_t> knownRevision;
};

/// Decodes a snapshot request payload.
std::expected<SnapshotRequestPayload, MessageError> DecodeSnapshotRequestPayload(
    const boost::json::object& payload);

/// State snapshot payload establishing a revision baseline.
struct StateSnapshotPayload {
    /// State area represented by the snapshot.
    std::string stateArea;
    /// Revision established by the snapshot.
    std::int64_t revision = 0;
    /// Human-readable event timestamp; not an ordering source.
    std::string occurredAt;
    /// State-area-specific snapshot data.
    boost::json::object data;
};

/// Decodes a state snapshot payload.
std::expected<StateSnapshotPayload, MessageError> DecodeStateSnapshotPayload(
    const boost::json::object& payload);

/// Encodes a state snapshot payload.
boost::json::object EncodeStateSnapshotPayload(const StateSnapshotPayload& payload);

/// State event payload advancing a state area from one revision to another.
struct StateEventPayload {
    /// State area represented by the event.
    std::string stateArea;
    /// Revision immediately preceding this event.
    std::int64_t baseRevision = 0;
    /// Revision established by this event.
    std::int64_t revision = 0;
    /// Human-readable event timestamp; not an ordering source.
    std::string occurredAt;
    /// State-area-specific event data.
    boost::json::object data;
};

/// Decodes a state event payload's structural fields.
std::expected<StateEventPayload, MessageError> DecodeStateEventPayload(
    const boost::json::object& payload);

/// Current and maximum values for one character resource pool.
struct ResourceValue {
    /// Current resource amount.
    double current = 0.0;
    /// Maximum resource amount.
    double maximum = 0.0;
};

/// Decoded character state-area data.
struct CharacterState {
    /// Character level, or unavailable when encoded as JSON null.
    std::optional<std::int64_t> level;
    /// Health resource values, or unavailable when encoded as JSON null.
    std::optional<ResourceValue> health;
    /// Magicka resource values, or unavailable when encoded as JSON null.
    std::optional<ResourceValue> magicka;
    /// Stamina resource values, or unavailable when encoded as JSON null.
    std::optional<ResourceValue> stamina;
};

/// Decodes character state-area data from a snapshot or event payload.
std::expected<CharacterState, MessageError> DecodeCharacterState(
    const boost::json::object& data);

/// Structured error information returned by the bridge.
struct ErrorPayload {
    /// Stable machine-readable error code.
    std::string code;
    /// Human-readable error description.
    std::string message;
    /// Whether retrying the failed operation is allowed.
    bool retryable = false;
    /// Optional structured error details; absent represents JSON null on decode.
    std::optional<boost::json::value> details;
};

/// Decodes an error payload.
std::expected<ErrorPayload, MessageError> DecodeErrorPayload(
    const boost::json::object& payload);

/// Encodes an error payload, always including its nullable details field.
boost::json::object EncodeErrorPayload(const ErrorPayload& payload);

/// Builds a complete error envelope and always returns a usable envelope.
/// Uses the `csprng-unavailable` sentinel message ID if secure ID generation
/// fails. `correlationId` identifies the message being answered or is null when
/// there is no correlation; `sessionId` is null before a session exists and is
/// otherwise the active session ID.
Envelope BuildErrorEnvelope(std::optional<std::string> correlationId,
                            std::optional<std::string> sessionId, std::string code,
                            std::string message, bool retryable);

}  // namespace dovahlink::protocol

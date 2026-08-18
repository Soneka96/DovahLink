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
inline constexpr std::string_view kPairingRequest = "pairing_request";
inline constexpr std::string_view kPairingStatus = "pairing_status";
inline constexpr std::string_view kPairingConfirm = "pairing_confirm";
inline constexpr std::string_view kPairingAck = "pairing_ack";
inline constexpr std::string_view kPairingOutcome = "pairing_outcome";
}  // namespace message_type

/// Identifiers for registered state areas.
namespace state_area {
inline constexpr std::string_view kCharacter = "character";
}  // namespace state_area

/// Reports a message-payload decoding failure.
using MessageError = DecodeError;

/// Decoded client authentication and identity data. `auth.method` is one of
/// `"one_time_local_token"` (developer authentication, `security.md`'s "Developer
/// authentication"), `"unpaired"` (no credential yet -- admits a trust-restricted session solely to
/// run the pairing flow), or `"trusted_device_credential"` (a persisted pairing credential, for an
/// ordinary reconnect). See `security.md`'s "Hello authentication and session trust tiers".
struct HelloPayload {
    /// Endpoint role declared by the client.
    std::string endpoint;
    /// Authentication method identifier.
    std::string authMethod;
    /// Authentication token supplied by the client. Absent only for `auth.method: "unpaired"`,
    /// which has no credential to present yet.
    std::optional<std::string> authToken;
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
    /// Kind of client identity established by this handshake: `"unpaired"` for a session admitted
    /// via `one_time_local_token` or the bootstrap `unpaired` auth method (trust-restricted until
    /// pairing succeeds), or `"paired"` for a session admitted via `trusted_device_credential`, or
    /// a restricted session upgraded in place by a successful pairing confirmation. See
    /// `security.md`'s "Hello authentication and session trust tiers".
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

// `pairing_request` carries no payload (like `ping`; see `BuildPong` in
// application/message_dispatcher.cpp for the established empty-payload precedent), so it has no
// dedicated struct or decode function.

/// Bridge report of pairing availability, sent in reply to `pairing_request`.
struct PairingStatusPayload {
    /// One of `"unavailable"`, `"available"`, `"in_progress"`.
    std::string state;
};

/// Decodes a pairing status payload.
std::expected<PairingStatusPayload, MessageError> DecodePairingStatusPayload(
    const boost::json::object& payload);

/// Encodes a pairing status payload.
boost::json::object EncodePairingStatusPayload(const PairingStatusPayload& payload);

/// Client submission of a pairing code, per `security.md`'s pairing handshake
/// (`CHALLENGE_ACTIVE -> PENDING_CREDENTIAL`).
struct PairingConfirmPayload {
    /// The six-digit code the user read from Skyrim and entered.
    std::string code;
    /// Optional presentation-only label for the resulting trusted client.
    std::optional<std::string> displayName;
};

/// Decodes a pairing confirm payload.
std::expected<PairingConfirmPayload, MessageError> DecodePairingConfirmPayload(
    const boost::json::object& payload);

/// Client's final confirmation, echoing back the credential it durably saved -- the wire form of
/// "final confirmation" in `security.md`'s pairing handshake (`PENDING_CREDENTIAL -> TRUSTED`).
struct PairingAckPayload {
    /// Hex-encoded credential the client received in a prior `credential_issued` outcome.
    std::string credential;
};

/// Decodes a pairing ack payload.
std::expected<PairingAckPayload, MessageError> DecodePairingAckPayload(
    const boost::json::object& payload);

/// Bridge reply to `pairing_confirm` or `pairing_ack`, distinguished by `outcome`.
struct PairingOutcomePayload {
    /// One of `"credential_issued"`, `"trusted"`, `"already_trusted"`, `"expired"`, `"invalid"`,
    /// `"rate_limited"`, `"pending_not_found"`.
    std::string outcome;
    /// Hex-encoded credential; present only for `"credential_issued"`, `"trusted"`, and
    /// `"already_trusted"`.
    std::optional<std::string> credential;
    /// Administration-only identifier; present only for `"trusted"` and `"already_trusted"`.
    std::optional<std::string> shortId;
    /// Echoed presentation-only label; present only alongside `credential`/`shortId` when the
    /// client supplied one.
    std::optional<std::string> displayName;
};

/// Decodes a pairing outcome payload.
std::expected<PairingOutcomePayload, MessageError> DecodePairingOutcomePayload(
    const boost::json::object& payload);

/// Encodes a pairing outcome payload.
boost::json::object EncodePairingOutcomePayload(const PairingOutcomePayload& payload);

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

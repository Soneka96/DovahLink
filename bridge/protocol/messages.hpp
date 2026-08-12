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

// The registered v1 DovahLink message payload codecs. See protocol/schema/README.md.
//
// Each Decode* function here validates and converts the payload of one registered
// message type (an already-decoded Envelope::payload, see envelope.hpp) into a
// DovahLink-owned value. This layer only validates structural shape (required
// fields, types); it does not enforce stateful business rules that depend on
// prior state, such as the state-event revision sequencing rules in
// protocol/schema/README.md ("revision must equal baseRevision + 1", stale/gap
// detection) -- those belong to the application layer (see
// ai/context/skse/architecture.md), which is the only layer that knows a
// session's current revision.
//
// occurredAt is validated as a non-empty string only. It is documented as "not
// an ordering source", so full RFC 3339 parsing is not required for Phase 1.
namespace dovahlink::protocol {

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

namespace state_area {
inline constexpr std::string_view kCharacter = "character";
}  // namespace state_area

using MessageError = DecodeError;

struct HelloPayload {
    std::string endpoint;
    std::vector<std::int64_t> supportedProtocolVersions;
    std::string authMethod;
    std::string authToken;
};
std::expected<HelloPayload, MessageError> DecodeHelloPayload(const boost::json::object& payload);

struct HelloAckPayload {
    std::int64_t selectedProtocolVersion = 0;
};
std::expected<HelloAckPayload, MessageError> DecodeHelloAckPayload(const boost::json::object& payload);
boost::json::object EncodeHelloAckPayload(const HelloAckPayload& payload);

struct Capability {
    std::string id;
    std::int64_t version = 0;
};
struct CapabilitiesPayload {
    std::vector<Capability> capabilities;
};
std::expected<CapabilitiesPayload, MessageError> DecodeCapabilitiesPayload(const boost::json::object& payload);
boost::json::object EncodeCapabilitiesPayload(const CapabilitiesPayload& payload);

struct SubscribePayload {
    std::vector<std::string> stateAreas;
};
std::expected<SubscribePayload, MessageError> DecodeSubscribePayload(const boost::json::object& payload);

struct SubscriptionAckPayload {
    std::vector<std::string> acceptedStateAreas;
    std::vector<std::string> rejectedStateAreas;
};
std::expected<SubscriptionAckPayload, MessageError> DecodeSubscriptionAckPayload(
    const boost::json::object& payload);
boost::json::object EncodeSubscriptionAckPayload(const SubscriptionAckPayload& payload);

struct SnapshotRequestPayload {
    std::string stateArea;
    std::optional<std::int64_t> knownRevision;
};
std::expected<SnapshotRequestPayload, MessageError> DecodeSnapshotRequestPayload(
    const boost::json::object& payload);

struct StateSnapshotPayload {
    std::string stateArea;
    std::int64_t revision = 0;
    std::string occurredAt;
    boost::json::object data;
};
std::expected<StateSnapshotPayload, MessageError> DecodeStateSnapshotPayload(
    const boost::json::object& payload);
boost::json::object EncodeStateSnapshotPayload(const StateSnapshotPayload& payload);

struct StateEventPayload {
    std::string stateArea;
    std::int64_t baseRevision = 0;
    std::int64_t revision = 0;
    std::string occurredAt;
    boost::json::object data;
};
std::expected<StateEventPayload, MessageError> DecodeStateEventPayload(const boost::json::object& payload);

// The character state area's resource pools (health/magicka/stamina).
struct ResourceValue {
    double current = 0.0;
    double maximum = 0.0;
};

// The character state area's data, decoded from a state_snapshot/state_event
// payload's `data` field when stateArea == state_area::kCharacter. health,
// magicka, and stamina are always nullopt in Phase 1 (see TASK.md).
struct CharacterState {
    std::optional<std::int64_t> level;
    std::optional<ResourceValue> health;
    std::optional<ResourceValue> magicka;
    std::optional<ResourceValue> stamina;
};
std::expected<CharacterState, MessageError> DecodeCharacterState(const boost::json::object& data);

struct ErrorPayload {
    std::string code;
    std::string message;
    bool retryable = false;
    // Present only when the source payload's "details" is present and non-null.
    std::optional<boost::json::value> details;
};
std::expected<ErrorPayload, MessageError> DecodeErrorPayload(const boost::json::object& payload);
// Always emits the `details` key, as `null` when absent, matching the
// canonical fixtures (protocol/fixtures/errors/*.json) -- "nullable and
// optional" (protocol/schema/README.md) means the value may be null, not
// that the key may be omitted from a v1 message this codebase produces.
boost::json::object EncodeErrorPayload(const ErrorPayload& payload);

// Builds a complete `error` envelope. `correlationId` is typically the
// messageId of the message being answered, or nullopt when there is no
// correlation -- including when the input could not even be decoded far
// enough to learn its own messageId (see protocol/schema/README.md:
// correlationId is "Message ID being answered, or null when there is no
// correlation"). `sessionId` is the caller's choice: nullopt for a
// pre-session rejection (e.g. answering hello), the active session's ID
// once one exists. `protocolVersion` is likewise the caller's choice: 0
// while still negotiating, the selected version afterward.
//
// Always succeeds: if the underlying messageId generation
// (security::GenerateOpaqueId) fails -- an unreachable-in-practice CSPRNG
// failure, see security/csprng.hpp -- this falls back to a fixed sentinel
// messageId rather than propagate the failure to the caller. An error
// report is already a degraded channel; every caller independently
// reimplementing the same "what if we can't even report the error"
// fallback would just duplicate this one decision.
Envelope BuildErrorEnvelope(std::optional<std::string> correlationId, std::int64_t protocolVersion,
                             std::optional<std::string> sessionId, std::string code, std::string message,
                             bool retryable);

}  // namespace dovahlink::protocol

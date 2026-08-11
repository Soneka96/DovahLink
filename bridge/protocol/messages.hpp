#pragma once

#include "protocol/decode_error.hpp"

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

struct Capability {
    std::string id;
    std::int64_t version = 0;
};
struct CapabilitiesPayload {
    std::vector<Capability> capabilities;
};
std::expected<CapabilitiesPayload, MessageError> DecodeCapabilitiesPayload(const boost::json::object& payload);

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

}  // namespace dovahlink::protocol

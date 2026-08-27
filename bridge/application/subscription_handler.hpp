#pragma once

#include "application/subscribe_result.hpp"
#include "protocol/envelope.hpp"

#include <optional>
#include <string>

namespace dovahlink::application {

///  Builds the bridge capabilities envelope for an authenticated session. No
///  capability is currently registered (protocol/schema/README.md's "Registered
///  state areas"), so the advertised list is always empty.
///  @param sessionId Server-issued session identifier.
///  @return Capabilities envelope, or no value if an identifier cannot be
///  generated.
[[nodiscard]] std::optional<protocol::Envelope>
BuildBridgeCapabilities(const std::string& sessionId);

///  Validates a client capabilities envelope. No capability is currently
///  registered, so any non-empty list is rejected.
///  @param capabilitiesEnvelope Decoded client capabilities message.
///  @param sessionId Authenticated session identifier.
///  @return Error envelope when validation fails; no value when accepted.
[[nodiscard]] std::optional<protocol::Envelope>
HandleClientCapabilities(const protocol::Envelope& capabilitiesEnvelope,
                         const std::string& sessionId);

///  Handles a subscription request. No state area is currently registered
///  (protocol/schema/README.md's "Registered state areas"), so every requested
///  area is rejected into the acknowledgement's `rejectedStateAreas` and no
///  snapshot is ever produced.
///  @param subscribeEnvelope Decoded client subscription request.
///  @param sessionId Authenticated session identifier.
///  @return Subscription acknowledgement with every requested area rejected.
[[nodiscard]] SubscribeResult
HandleSubscribe(const protocol::Envelope& subscribeEnvelope,
                const std::string& sessionId);

///  Handles a request for a fresh state snapshot. No state area is currently
///  registered, so every request is rejected as `unsupported_capability`.
///  @param snapshotRequestEnvelope Decoded client snapshot request.
///  @param sessionId Authenticated session identifier.
///  @return An `unsupported_capability` error envelope, or `malformed_message`
///  for an invalid payload.
[[nodiscard]] protocol::Envelope
HandleSnapshotRequest(const protocol::Envelope& snapshotRequestEnvelope,
                      const std::string& sessionId);

} //  namespace dovahlink::application

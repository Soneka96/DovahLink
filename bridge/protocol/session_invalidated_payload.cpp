#include "protocol/session_invalidated_payload.hpp"

#include "protocol/constants.hpp"

#include <utility>

namespace dovahlink::protocol {

boost::json::object
EncodeSessionInvalidatedPayload(const SessionInvalidatedPayload &payload) {
  boost::json::object obj;
  obj["reason"] = payload.reason;
  return obj;
}

Envelope BuildSessionInvalidatedEnvelope(std::optional<std::string> sessionId,
                                         std::string reason) {
  boost::json::object payload = EncodeSessionInvalidatedPayload(
      SessionInvalidatedPayload{.reason = reason});
  auto envelope =
      BuildEnvelope(std::string(message_type::kSessionInvalidated), sessionId,
                    /*correlationId=*/std::nullopt, payload);
  if (envelope.has_value()) {
    return std::move(*envelope);
  }
  // GenerateOpaqueId failed inside BuildEnvelope -- unreachable in practice
  // (security/csprng.hpp), matching BuildErrorEnvelope's own fallback.
  return Envelope{
      .messageType = std::string(message_type::kSessionInvalidated),
      .messageId = "csprng-unavailable",
      .sessionId = std::move(sessionId),
      .correlationId = std::nullopt,
      .payload = std::move(payload),
  };
}

} // namespace dovahlink::protocol

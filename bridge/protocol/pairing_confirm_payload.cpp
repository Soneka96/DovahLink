#include "protocol/pairing_confirm_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <utility>

namespace dovahlink::protocol {

std::expected<PairingConfirmPayload, MessageError>
DecodePairingConfirmPayload(const boost::json::object &payload) {
  auto code = DecodeNonEmptyString(RequireField(payload, "code"), "code");
  if (!code) {
    return std::unexpected(code.error());
  }
  auto displayName =
      DecodeOptionalString(RequireField(payload, "displayName"), "displayName");
  if (!displayName) {
    return std::unexpected(displayName.error());
  }
  return PairingConfirmPayload{.code = std::move(*code),
                               .displayName = std::move(*displayName)};
}

} // namespace dovahlink::protocol

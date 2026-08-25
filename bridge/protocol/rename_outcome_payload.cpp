#include "protocol/rename_outcome_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <algorithm>
#include <array>
#include <string_view>
#include <utility>

namespace dovahlink::protocol {

namespace {

/// Registered `rename_outcome.outcome` values.
constexpr std::array<std::string_view, 3> kValidRenameOutcomes = {
    "renamed",
    "invalid_display_name",
    "not_trusted",
};

} // namespace

std::expected<RenameOutcomePayload, MessageError>
DecodeRenameOutcomePayload(const boost::json::object &payload) {
  auto outcome =
      DecodeNonEmptyString(RequireField(payload, "outcome"), "outcome");
  if (!outcome) {
    return std::unexpected(outcome.error());
  }
  if (std::ranges::find(kValidRenameOutcomes, *outcome) ==
      kValidRenameOutcomes.end()) {
    return Fail("outcome must be one of the registered rename outcomes");
  }

  auto displayName =
      DecodeOptionalString(RequireField(payload, "displayName"), "displayName");
  if (!displayName) {
    return std::unexpected(displayName.error());
  }

  return RenameOutcomePayload{
      .outcome = std::move(*outcome),
      .displayName = std::move(*displayName),
  };
}

boost::json::object
EncodeRenameOutcomePayload(const RenameOutcomePayload &payload) {
  boost::json::object obj;
  obj["outcome"] = payload.outcome;
  obj["displayName"] = payload.displayName.has_value()
                           ? boost::json::value(*payload.displayName)
                           : boost::json::value(nullptr);
  return obj;
}

} // namespace dovahlink::protocol

#pragma once

#include "protocol/decode_error.hpp"

#include <boost/json/value.hpp>

#include <cstdint>
#include <expected>
#include <string>
#include <string_view>

// Small, reusable field-level JSON decoders shared by the envelope codec and the
// registered message payload codecs (see envelope.hpp, messages.hpp).
namespace dovahlink::protocol {

std::expected<std::string, DecodeError> DecodeNonEmptyString(const boost::json::value* value,
                                                               std::string_view fieldName);

std::expected<std::int64_t, DecodeError> DecodeNonNegativeInt(const boost::json::value* value,
                                                                std::string_view fieldName);

}  // namespace dovahlink::protocol

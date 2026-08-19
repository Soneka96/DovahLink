#pragma once

#include "protocol/decode_error.hpp"
#include "protocol/resource_value.hpp"

#include <boost/json/object.hpp>

#include <cstdint>
#include <expected>
#include <optional>

namespace dovahlink::protocol {

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
std::expected<CharacterState, MessageError> DecodeCharacterState(const boost::json::object& data);

}  // namespace dovahlink::protocol

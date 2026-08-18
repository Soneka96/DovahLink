#include "protocol/character_state.hpp"

#include "protocol/json_field_decoders.hpp"

namespace dovahlink::protocol {

namespace {

/// Decodes a resource field as JSON null or a numeric current/maximum pair.
std::expected<std::optional<ResourceValue>, MessageError> DecodeResourceValue(const boost::json::value* value,
                                                                               std::string_view fieldName) {
    if (!value) {
        return Fail("missing required field: " + std::string(fieldName));
    }
    if (value->is_null()) {
        return std::optional<ResourceValue>{};
    }
    if (!value->is_object()) {
        return Fail(std::string(fieldName) + " must be an object or null");
    }
    const boost::json::object& obj = value->get_object();

    const boost::json::value* currentValue = RequireField(obj, "current");
    const boost::json::value* maximumValue = RequireField(obj, "maximum");
    if (!currentValue || !currentValue->is_number() || !maximumValue || !maximumValue->is_number()) {
        return Fail(std::string(fieldName) + ".current and .maximum must be numbers");
    }

    return std::optional<ResourceValue>{ResourceValue{
        .current = currentValue->to_number<double>(),
        .maximum = maximumValue->to_number<double>(),
    }};
}

}  // namespace

std::expected<CharacterState, MessageError> DecodeCharacterState(const boost::json::object& data) {
    const boost::json::value* levelValue = RequireField(data, "level");
    if (!levelValue) {
        return Fail("missing required field: level");
    }
    std::optional<std::int64_t> level;
    if (!levelValue->is_null()) {
        auto decodedLevel = DecodeNonNegativeInt(levelValue, "level");
        if (!decodedLevel) {
            return std::unexpected(decodedLevel.error());
        }
        level = *decodedLevel;
    }

    auto health = DecodeResourceValue(RequireField(data, "health"), "health");
    if (!health) {
        return std::unexpected(health.error());
    }
    auto magicka = DecodeResourceValue(RequireField(data, "magicka"), "magicka");
    if (!magicka) {
        return std::unexpected(magicka.error());
    }
    auto stamina = DecodeResourceValue(RequireField(data, "stamina"), "stamina");
    if (!stamina) {
        return std::unexpected(stamina.error());
    }

    return CharacterState{
        .level = level,
        .health = *health,
        .magicka = *magicka,
        .stamina = *stamina,
    };
}

}  // namespace dovahlink::protocol

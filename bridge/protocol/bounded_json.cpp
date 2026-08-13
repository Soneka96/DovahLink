#include "protocol/bounded_json.hpp"

#include "security/limits.hpp"

#include <boost/json/parse.hpp>
#include <boost/json/parse_options.hpp>
#include <boost/system/error_code.hpp>

#include <optional>

namespace dovahlink::protocol {

namespace {

/**
 * @brief Checks a JSON value and its descendants against configured size limits.
 *
 * @param value JSON value to validate, including nested arrays, objects, and object keys.
 * @return The first detected limit violation, or `std::nullopt` when all limits are satisfied.
 */
std::optional<BoundedJsonError> FindLimitViolation(const boost::json::value& value) {
    switch (value.kind()) {
        case boost::json::kind::string: {
            if (value.get_string().size() > security::kMaxStringLengthBytes) {
                return BoundedJsonError::kStringTooLong;
            }
            return std::nullopt;
        }
        case boost::json::kind::array: {
            const auto& arr = value.get_array();
            if (arr.size() > security::kMaxArrayItems) {
                return BoundedJsonError::kArrayTooLong;
            }
            for (const auto& item : arr) {
                if (auto violation = FindLimitViolation(item)) {
                    return violation;
                }
            }
            return std::nullopt;
        }
        case boost::json::kind::object: {
            const auto& obj = value.get_object();
            if (obj.size() > security::kMaxObjectMembers) {
                return BoundedJsonError::kTooManyObjectMembers;
            }
            for (const auto& member : obj) {
                if (member.key().size() > security::kMaxStringLengthBytes) {
                    return BoundedJsonError::kStringTooLong;
                }
                if (auto violation = FindLimitViolation(member.value())) {
                    return violation;
                }
            }
            return std::nullopt;
        }
        default:
            return std::nullopt;
    }
}

}  /**
 * @brief Parses JSON while enforcing inbound size, nesting, and value limits.
 *
 * @param text JSON document to parse.
 * @return Parsed JSON value on success; otherwise the applicable
 *         `BoundedJsonError` for an oversized frame, invalid JSON, or a
 *         limit violation.
 */

std::expected<boost::json::value, BoundedJsonError> ParseBoundedJson(std::string_view text) {
    if (text.size() > security::kMaxInboundFrameBytes) {
        return std::unexpected(BoundedJsonError::kFrameTooLarge);
    }

    boost::json::parse_options options;
    options.max_depth = static_cast<unsigned>(security::kMaxJsonNestingDepth);

    boost::system::error_code ec;
    boost::json::value parsed = boost::json::parse(text, ec, {}, options);
    if (ec) {
        return std::unexpected(BoundedJsonError::kInvalidJson);
    }

    if (auto violation = FindLimitViolation(parsed)) {
        return std::unexpected(*violation);
    }

    return parsed;
}

}  // namespace dovahlink::protocol

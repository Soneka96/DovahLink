#include "protocol/hello_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <algorithm>
#include <array>
#include <utility>

namespace dovahlink::protocol {

namespace {

constexpr std::array<std::string_view, 3> kValidAuthMethods = {
    "one_time_local_token",
    "unpaired",
    "trusted_device_credential",
};

}  // namespace

std::expected<HelloPayload, MessageError> DecodeHelloPayload(const boost::json::object& payload) {
    auto endpoint = DecodeNonEmptyString(RequireField(payload, "endpoint"), "endpoint");
    if (!endpoint) {
        return std::unexpected(endpoint.error());
    }
    if (*endpoint != "client") {
        return Fail("endpoint must be 'client'");
    }

    const boost::json::value* authValue = RequireField(payload, "auth");
    if (!authValue || !authValue->is_object()) {
        return Fail("auth must be an object");
    }
    const boost::json::object& authObj = authValue->get_object();

    auto authMethod = DecodeNonEmptyString(RequireField(authObj, "method"), "auth.method");
    if (!authMethod) {
        return std::unexpected(authMethod.error());
    }
    if (std::ranges::find(kValidAuthMethods, *authMethod) == kValidAuthMethods.end()) {
        return Fail("auth.method must be one of the registered authentication methods");
    }

    // "unpaired" bootstraps a session with no credential to present yet; the other two methods
    // both require one (security.md's "Hello authentication and session trust tiers").
    std::optional<std::string> authToken;
    if (*authMethod == "unpaired") {
        if (RequireField(authObj, "token")) {
            return Fail("auth.token must be absent for auth.method 'unpaired'");
        }
    } else {
        auto decodedToken = DecodeNonEmptyString(RequireField(authObj, "token"), "auth.token");
        if (!decodedToken) {
            return std::unexpected(decodedToken.error());
        }
        authToken = std::move(*decodedToken);
    }

    auto clientId = DecodeNonEmptyString(RequireField(payload, "clientId"), "clientId");
    if (!clientId) {
        return std::unexpected(clientId.error());
    }

    return HelloPayload{
        .endpoint = std::move(*endpoint),
        .authMethod = std::move(*authMethod),
        .authToken = std::move(authToken),
        .clientId = std::move(*clientId),
    };
}

}  // namespace dovahlink::protocol

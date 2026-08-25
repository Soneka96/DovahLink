#pragma once

#include "protocol/envelope.hpp"
#include "security/test_token.hpp"

#include <boost/json/object.hpp>

#include <optional>
#include <string>
#include <utility>

namespace dovahlink::application::test_support {

///  Builds a representative client `hello` envelope for application tests.
///
///  The optional credential is omitted when it is `std::nullopt`, which allows
///  callers to exercise authentication methods whose payload has no token or a
///  missing-token validation path without rebuilding the envelope shape.
inline protocol::Envelope BuildHelloEnvelope(
    std::optional<std::string> credential =
        std::string(security::kValidHexToken),
    std::string messageId = "message-hello-1",
    std::optional<std::string> clientId = std::string("client-1"),
    std::string authMethod = "one_time_local_token") {
    boost::json::object payload;
    payload["endpoint"] = "client";
    if (clientId.has_value()) {
        payload["clientId"] = *clientId;
    }

    boost::json::object auth;
    auth["method"] = std::move(authMethod);
    if (credential.has_value()) {
        auth["token"] = *credential;
    }
    payload["auth"] = std::move(auth);

    return protocol::Envelope{
        .messageType = "hello",
        .messageId = std::move(messageId),
        .sessionId = std::nullopt,
        .correlationId = std::nullopt,
        .payload = std::move(payload),
    };
}

} //  namespace dovahlink::application::test_support

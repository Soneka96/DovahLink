#pragma once

#include "protocol/envelope.hpp"
#include "security/test_token.hpp"

#include <boost/json/object.hpp>

#include <optional>
#include <string>
#include <utility>

namespace dovahlink::application::test_support {

///  Builds a representative authenticated envelope with caller-controlled
///  protocol type, identity, and payload values.
inline protocol::Envelope BuildEnvelope(
    std::string messageType = "ping", std::string messageId = "message-1",
    std::optional<std::string> sessionId = std::string("session-1"),
    std::optional<std::string> correlationId = std::nullopt,
    boost::json::object payload = {}) {
    return protocol::Envelope{
        .messageType = std::move(messageType),
        .messageId = std::move(messageId),
        .sessionId = std::move(sessionId),
        .correlationId = std::move(correlationId),
        .payload = std::move(payload),
    };
}

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

///  Builds a representative `pairing_request` envelope.
inline protocol::Envelope BuildPairingRequestEnvelope(
    std::string messageId = "message-request-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    return BuildEnvelope("pairing_request", std::move(messageId),
                         std::move(sessionId));
}

///  Builds a representative `pairing_confirm` envelope with an optional display
///  name.
inline protocol::Envelope BuildPairingConfirmEnvelope(
    const std::string& code, std::optional<std::string> displayName = std::nullopt,
    std::string messageId = "message-confirm-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    boost::json::object payload;
    payload["code"] = code;
    payload["displayName"] = displayName.has_value()
                                 ? boost::json::value(*displayName)
                                 : boost::json::value(nullptr);
    return BuildEnvelope("pairing_confirm", std::move(messageId),
                         std::move(sessionId), std::nullopt, std::move(payload));
}

///  Builds a representative `pairing_ack` envelope for a hex credential.
inline protocol::Envelope BuildPairingAckEnvelope(
    const std::string& hexCredential, std::string messageId = "message-ack-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    boost::json::object payload;
    payload["credential"] = hexCredential;
    return BuildEnvelope("pairing_ack", std::move(messageId),
                         std::move(sessionId), std::nullopt, std::move(payload));
}

///  Builds a representative `pairing_renotify` envelope.
inline protocol::Envelope BuildPairingRenotifyEnvelope(
    std::string messageId = "message-renotify-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    return BuildEnvelope("pairing_renotify", std::move(messageId),
                         std::move(sessionId));
}

///  Builds a representative `pairing_cancel` envelope.
inline protocol::Envelope BuildPairingCancelEnvelope(
    std::string messageId = "message-cancel-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    return BuildEnvelope("pairing_cancel", std::move(messageId),
                         std::move(sessionId));
}

///  Builds a representative `rename_request` envelope with a display name.
inline protocol::Envelope BuildRenameRequestEnvelope(
    const std::string& displayName, std::string messageId = "message-rename-1",
    std::optional<std::string> sessionId = std::string("session-1")) {
    boost::json::object payload;
    payload["displayName"] = displayName;
    return BuildEnvelope("rename_request", std::move(messageId),
                         std::move(sessionId), std::nullopt, std::move(payload));
}

} //  namespace dovahlink::application::test_support

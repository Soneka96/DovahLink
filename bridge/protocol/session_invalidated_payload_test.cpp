#include "protocol/session_invalidated_payload.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>

using dovahlink::protocol::BuildSessionInvalidatedEnvelope;
using dovahlink::protocol::EncodeSessionInvalidatedPayload;
using dovahlink::protocol::SessionInvalidatedPayload;

TEST_CASE("EncodeSessionInvalidatedPayload encodes only the reason field", "[protocol][session_invalidated_payload]") {
    auto encoded = EncodeSessionInvalidatedPayload(SessionInvalidatedPayload{.reason = "revoked"});

    CHECK(encoded.size() == 1);
    REQUIRE(encoded.contains("reason"));
    CHECK(encoded.at("reason").as_string() == "revoked");
}

TEST_CASE("BuildSessionInvalidatedEnvelope sets the message type and a non-empty messageId",
          "[protocol][session_invalidated_payload]") {
    auto envelope = BuildSessionInvalidatedEnvelope(/*sessionId=*/std::string("session-1"), "blocked");

    CHECK(envelope.messageType == "session_invalidated");
    CHECK_FALSE(envelope.messageId.empty());
}

TEST_CASE("BuildSessionInvalidatedEnvelope accepts a nullopt sessionId", "[protocol][session_invalidated_payload]") {
    auto envelope = BuildSessionInvalidatedEnvelope(/*sessionId=*/std::nullopt, "blocked");

    CHECK_FALSE(envelope.sessionId.has_value());
}

TEST_CASE("BuildSessionInvalidatedEnvelope carries the caller's sessionId through unchanged",
          "[protocol][session_invalidated_payload]") {
    auto envelope = BuildSessionInvalidatedEnvelope(std::string("session-42"), "trust_reset");

    REQUIRE(envelope.sessionId.has_value());
    CHECK(*envelope.sessionId == "session-42");
}

TEST_CASE("BuildSessionInvalidatedEnvelope always sets a null correlationId",
          "[protocol][session_invalidated_payload]") {
    auto envelope = BuildSessionInvalidatedEnvelope(std::string("session-1"), "factory_reset");

    CHECK_FALSE(envelope.correlationId.has_value());
}

TEST_CASE("BuildSessionInvalidatedEnvelope's payload decodes to the same reason",
          "[protocol][session_invalidated_payload]") {
    auto envelope = BuildSessionInvalidatedEnvelope(std::string("session-1"), "revoked");

    REQUIRE(envelope.payload.contains("reason"));
    CHECK(envelope.payload.at("reason").as_string() == "revoked");
}

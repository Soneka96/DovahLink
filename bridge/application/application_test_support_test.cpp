#include "application/application_test_support.hpp"

#include "security/test_token.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/parse.hpp>

#include <string>

using dovahlink::application::test_support::BuildHelloEnvelope;

TEST_CASE("BuildHelloEnvelope creates the representative one-time-token hello",
          "[application][test_support]") {
    auto hello = BuildHelloEnvelope();

    CHECK(hello.messageType == "hello");
    CHECK(hello.messageId == "message-hello-1");
    CHECK_FALSE(hello.sessionId.has_value());
    CHECK_FALSE(hello.correlationId.has_value());
    CHECK_FALSE(hello.bridgeInstanceId.has_value());
    CHECK_FALSE(hello.playContextId.has_value());
    CHECK_FALSE(hello.clientId.has_value());

    REQUIRE(hello.payload.contains("endpoint"));
    CHECK(hello.payload.at("endpoint").as_string() == "client");
    REQUIRE(hello.payload.contains("clientId"));
    CHECK(hello.payload.at("clientId").as_string() == "client-1");

    const auto& auth = hello.payload.at("auth").as_object();
    CHECK(auth.at("method").as_string() == "one_time_local_token");
    CHECK(auth.at("token").as_string() ==
          dovahlink::security::kValidHexToken);
}

TEST_CASE("BuildHelloEnvelope supports authentication and identity overrides",
          "[application][test_support]") {
    auto unpaired = BuildHelloEnvelope(std::nullopt, "message-hello-2",
                                       "client-2", "unpaired");
    CHECK(unpaired.messageId == "message-hello-2");
    CHECK(unpaired.payload.at("clientId").as_string() == "client-2");
    const auto& unpairedAuth = unpaired.payload.at("auth").as_object();
    CHECK(unpairedAuth.at("method").as_string() == "unpaired");
    CHECK_FALSE(unpairedAuth.if_contains("token"));

    auto trusted = BuildHelloEnvelope(std::string("credential-1"),
                                      "message-hello-3", std::nullopt,
                                      "trusted_device_credential");
    CHECK_FALSE(trusted.payload.if_contains("clientId"));
    const auto& trustedAuth = trusted.payload.at("auth").as_object();
    CHECK(trustedAuth.at("method").as_string() ==
          "trusted_device_credential");
    CHECK(trustedAuth.at("token").as_string() == "credential-1");
}

TEST_CASE("BuildHelloEnvelope returns independent values per call",
          "[application][test_support]") {
    auto first = BuildHelloEnvelope();
    auto second = BuildHelloEnvelope();

    first.messageId = "changed-message";
    first.payload.at("clientId") = "changed-client";

    CHECK(second.messageId == "message-hello-1");
    CHECK(second.payload.at("clientId").as_string() == "client-1");
}

TEST_CASE("BuildHelloEnvelope preserves its representative wire shape",
          "[application][test_support]") {
    auto encoded = dovahlink::protocol::EncodeEnvelope(BuildHelloEnvelope());
    auto parsed = boost::json::parse(encoded);
    auto decoded = dovahlink::protocol::DecodeEnvelope(parsed);

    REQUIRE(decoded.has_value());
    CHECK(decoded->messageType == "hello");
    CHECK(decoded->messageId == "message-hello-1");
    CHECK_FALSE(decoded->sessionId.has_value());
    CHECK_FALSE(decoded->correlationId.has_value());
    CHECK_FALSE(decoded->bridgeInstanceId.has_value());
    CHECK_FALSE(decoded->playContextId.has_value());
    CHECK_FALSE(decoded->clientId.has_value());
    CHECK(decoded->payload.at("clientId").as_string() == "client-1");
}

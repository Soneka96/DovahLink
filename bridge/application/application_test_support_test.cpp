#include "application/application_test_support.hpp"

#include "security/test_token.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/parse.hpp>

#include <chrono>
#include <cstdint>
#include <string>
#include <vector>

using dovahlink::application::test_support::BuildEnvelope;
using dovahlink::application::test_support::BuildHelloEnvelope;
using dovahlink::application::test_support::BuildKnownDeviceRecord;
using dovahlink::application::test_support::BuildPairingAckEnvelope;
using dovahlink::application::test_support::BuildPairingCancelEnvelope;
using dovahlink::application::test_support::BuildPairingConfirmEnvelope;
using dovahlink::application::test_support::BuildPairingRenotifyEnvelope;
using dovahlink::application::test_support::BuildPairingRequestEnvelope;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::BuildRenameRequestEnvelope;

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

TEST_CASE("BuildEnvelope supports authenticated payload overrides",
          "[application][test_support]") {
    auto payload = boost::json::parse(
                       R"({"stateAreas": ["example_area"]})")
                       .get_object();
    auto envelope = BuildEnvelope("subscribe", "message-sub-2", "session-2",
                                  "request-1", std::move(payload));

    CHECK(envelope.messageType == "subscribe");
    CHECK(envelope.messageId == "message-sub-2");
    REQUIRE(envelope.sessionId.has_value());
    CHECK(*envelope.sessionId == "session-2");
    REQUIRE(envelope.correlationId.has_value());
    CHECK(*envelope.correlationId == "request-1");
    CHECK(envelope.payload.at("stateAreas").as_array().size() == 1);
    CHECK_FALSE(envelope.bridgeInstanceId.has_value());
    CHECK_FALSE(envelope.playContextId.has_value());
    CHECK_FALSE(envelope.clientId.has_value());
}

TEST_CASE("named application fixture builders use representative protocol shapes",
          "[application][test_support]") {
    auto request = BuildPairingRequestEnvelope();
    CHECK(request.messageType == "pairing_request");
    CHECK(request.payload.empty());

    auto confirm = BuildPairingConfirmEnvelope("123456", "My PC");
    CHECK(confirm.messageType == "pairing_confirm");
    CHECK(confirm.payload.at("code").as_string() == "123456");
    CHECK(confirm.payload.at("displayName").as_string() == "My PC");

    auto ack = BuildPairingAckEnvelope("credential-1");
    CHECK(ack.messageType == "pairing_ack");
    CHECK(ack.payload.at("credential").as_string() == "credential-1");

    auto renotify = BuildPairingRenotifyEnvelope();
    CHECK(renotify.messageType == "pairing_renotify");
    CHECK(renotify.payload.empty());

    auto cancel = BuildPairingCancelEnvelope();
    CHECK(cancel.messageType == "pairing_cancel");
    CHECK(cancel.payload.empty());

    auto rename = BuildRenameRequestEnvelope("New Name");
    CHECK(rename.messageType == "rename_request");
    CHECK(rename.payload.at("displayName").as_string() == "New Name");
}

TEST_CASE("BuildPlayContext returns a fresh context with the requested ID",
          "[application][test_support]") {
    auto first = BuildPlayContext("context-1");
    auto second = BuildPlayContext("context-1");

    REQUIRE(first);
    REQUIRE(second);
    CHECK(first->id == "context-1");
    CHECK(second->id == "context-1");
    CHECK(first.get() != second.get());
}

TEST_CASE("BuildKnownDeviceRecord applies representative state defaults",
          "[application][test_support]") {
    auto trusted = BuildKnownDeviceRecord();
    auto blocked = BuildKnownDeviceRecord(
        "client-2", "22222", std::nullopt,
        dovahlink::security::KnownDeviceState::kBlocked, 2);

    CHECK(trusted.clientId == "client-1");
    CHECK(trusted.shortId == "11111");
    CHECK(trusted.credential == std::vector<std::uint8_t>{1, 2});
    CHECK(trusted.state == dovahlink::security::KnownDeviceState::kTrusted);
    CHECK(blocked.clientId == "client-2");
    CHECK(blocked.credential.empty());
    CHECK(blocked.state == dovahlink::security::KnownDeviceState::kBlocked);
    CHECK(blocked.createdAt ==
          std::chrono::system_clock::time_point(std::chrono::seconds(2)));
}

#include "protocol/hello_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("hello fixture decodes to the expected HelloPayload", "[protocol][hello_payload]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->endpoint == "client");
    CHECK(hello->authMethod == "one_time_local_token");
    REQUIRE(hello->authToken.has_value());
    CHECK_FALSE(hello->authToken->empty());
    CHECK(hello->clientId == "client-1");
}

TEST_CASE("hello is rejected when clientId is null", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": null,
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is missing", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello-unpaired fixture decodes with no authToken", "[protocol][hello_payload]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-unpaired.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->authMethod == "unpaired");
    CHECK_FALSE(hello->authToken.has_value());
    CHECK(hello->clientId == "client-1");
}

TEST_CASE("hello-trusted-device-credential fixture decodes with an authToken", "[protocol][hello_payload]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-trusted-device-credential.json");
    auto hello = dovahlink::protocol::DecodeHelloPayload(envelope.payload);
    REQUIRE(hello.has_value());
    CHECK(hello->authMethod == "trusted_device_credential");
    REQUIRE(hello->authToken.has_value());
    CHECK_FALSE(hello->authToken->empty());
}

TEST_CASE("hello is rejected when auth.method is unrecognized", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "smoke-signal", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is unpaired but a token is present", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "unpaired", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is unpaired but token is present as JSON null",
          "[protocol][hello_payload]") {
    // A present-but-null key is a distinct wire case from an absent key: if_contains finds it
    // either way, so the rejection must not depend on the key being entirely missing.
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "unpaired", "token": null}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is trusted_device_credential but no token is present",
          "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "trusted_device_credential"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when endpoint is not 'client'", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "bridge", "clientId": "client-1",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth.method is not 'one_time_local_token'", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "client-1",
            "auth": {"method": "password", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when auth is missing", "[protocol][hello_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"endpoint": "client", "clientId": "client-1"})").get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is present but not a string", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": 5,
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

TEST_CASE("hello is rejected when clientId is an empty string", "[protocol][hello_payload]") {
    boost::json::object payload = boost::json::parse(
                                      R"({"endpoint": "client", "clientId": "",
            "auth": {"method": "one_time_local_token", "token": "t"}})")
                                      .get_object();
    auto hello = dovahlink::protocol::DecodeHelloPayload(payload);
    REQUIRE_FALSE(hello.has_value());
}

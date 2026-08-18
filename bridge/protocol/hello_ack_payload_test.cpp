#include "protocol/hello_ack_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("hello-ack fixture decodes to the expected HelloAckPayload", "[protocol][hello_ack_payload]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-ack.json");
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(helloAck.has_value());
    CHECK(helloAck->bridgeVersion == "0.2.0");
    CHECK(helloAck->clientIdentityKind == "unpaired");
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is null", "[protocol][hello_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "0.1.0", "clientIdentityKind": null})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is an empty string", "[protocol][hello_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "0.1.0", "clientIdentityKind": ""})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is null", "[protocol][hello_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": null, "clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is not a string", "[protocol][hello_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": 1, "clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is present but not a string",
          "[protocol][hello_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "0.1.0", "clientIdentityKind": 5})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is missing", "[protocol][hello_ack_payload]") {
    boost::json::object payload = boost::json::parse(R"({"clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when bridgeVersion is empty", "[protocol][hello_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"bridgeVersion": "", "clientIdentityKind": "unpaired"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("hello_ack is rejected when clientIdentityKind is missing", "[protocol][hello_ack_payload]") {
    boost::json::object payload = boost::json::parse(R"({"bridgeVersion": "0.1.0"})").get_object();
    auto helloAck = dovahlink::protocol::DecodeHelloAckPayload(payload);
    REQUIRE_FALSE(helloAck.has_value());
}

TEST_CASE("EncodeHelloAckPayload round-trips the hello-ack fixture's payload", "[protocol][hello_ack_payload]") {
    auto envelope = DecodeFixtureEnvelope("connection/hello-ack.json");
    auto original = dovahlink::protocol::DecodeHelloAckPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeHelloAckPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeHelloAckPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->bridgeVersion == original->bridgeVersion);
    CHECK(roundTripped->clientIdentityKind == original->clientIdentityKind);
}

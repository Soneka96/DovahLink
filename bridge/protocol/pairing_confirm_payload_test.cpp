#include "protocol/pairing_confirm_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE(
    "pairing-confirm fixture decodes to the expected PairingConfirmPayload",
    "[protocol][pairing_confirm_payload]") {
    auto envelope = DecodeFixtureEnvelope("pairing/pairing-confirm.json");
    auto confirm =
        dovahlink::protocol::DecodePairingConfirmPayload(envelope.payload);
    REQUIRE(confirm.has_value());
    CHECK(confirm->code == "123456");
    REQUIRE(confirm->displayName.has_value());
    CHECK(*confirm->displayName == "My PC");
}

TEST_CASE("pairing_confirm decodes a null displayName as absent",
          "[protocol][pairing_confirm_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"code": "123456", "displayName": null})")
            .get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE(confirm.has_value());
    CHECK_FALSE(confirm->displayName.has_value());
}

TEST_CASE("pairing_confirm decodes a missing displayName as absent",
          "[protocol][pairing_confirm_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"code": "123456"})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE(confirm.has_value());
    CHECK_FALSE(confirm->displayName.has_value());
}

TEST_CASE("pairing_confirm is rejected when code is missing",
          "[protocol][pairing_confirm_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"displayName": "My PC"})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing_confirm is rejected when code is an empty string",
          "[protocol][pairing_confirm_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"code": ""})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing_confirm is rejected when code has the wrong type",
          "[protocol][pairing_confirm_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"code": 123456})").get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

TEST_CASE("pairing_confirm is rejected when displayName has the wrong type",
          "[protocol][pairing_confirm_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"code": "123456", "displayName": 42})")
            .get_object();
    auto confirm = dovahlink::protocol::DecodePairingConfirmPayload(payload);
    REQUIRE_FALSE(confirm.has_value());
}

#include "protocol/subscription_ack_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <string>
#include <vector>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("subscription-ack fixture decodes to the expected SubscriptionAckPayload",
          "[protocol][subscription_ack_payload]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/subscription-ack.json");
    auto subscriptionAck = dovahlink::protocol::DecodeSubscriptionAckPayload(envelope.payload);
    REQUIRE(subscriptionAck.has_value());
    CHECK(subscriptionAck->acceptedStateAreas == std::vector<std::string>{"character"});
    CHECK(subscriptionAck->rejectedStateAreas.empty());
}

TEST_CASE("subscription_ack is rejected when rejectedStateAreas is the wrong type",
          "[protocol][subscription_ack_payload]") {
    boost::json::object payload =
        boost::json::parse(R"({"acceptedStateAreas": ["character"], "rejectedStateAreas": "none"})").get_object();
    auto subscriptionAck = dovahlink::protocol::DecodeSubscriptionAckPayload(payload);
    REQUIRE_FALSE(subscriptionAck.has_value());
}

TEST_CASE("EncodeSubscriptionAckPayload round-trips the subscription-ack fixture's payload",
          "[protocol][subscription_ack_payload]") {
    auto envelope = DecodeFixtureEnvelope("subscriptions/subscription-ack.json");
    auto original = dovahlink::protocol::DecodeSubscriptionAckPayload(envelope.payload);
    REQUIRE(original.has_value());

    boost::json::object encoded = dovahlink::protocol::EncodeSubscriptionAckPayload(*original);
    auto roundTripped = dovahlink::protocol::DecodeSubscriptionAckPayload(encoded);

    REQUIRE(roundTripped.has_value());
    CHECK(roundTripped->acceptedStateAreas == original->acceptedStateAreas);
    CHECK(roundTripped->rejectedStateAreas == original->rejectedStateAreas);
}

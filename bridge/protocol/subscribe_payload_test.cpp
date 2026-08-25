#include "protocol/subscribe_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <string>
#include <vector>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("subscribe fixture decodes to the expected SubscribePayload",
          "[protocol][subscribe_payload]") {
  auto envelope = DecodeFixtureEnvelope("subscriptions/subscribe.json");
  auto subscribe =
      dovahlink::protocol::DecodeSubscribePayload(envelope.payload);
  REQUIRE(subscribe.has_value());
  CHECK(subscribe->stateAreas == std::vector<std::string>{"example_area"});
}

TEST_CASE("subscribe is rejected when stateAreas contains a non-string item",
          "[protocol][subscribe_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"stateAreas": [1]})").get_object();
  auto subscribe = dovahlink::protocol::DecodeSubscribePayload(payload);
  REQUIRE_FALSE(subscribe.has_value());
}

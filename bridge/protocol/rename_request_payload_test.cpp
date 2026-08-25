#include "protocol/rename_request_payload.hpp"

#include "protocol/fixture_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("rename-request fixture decodes to the expected RenameRequestPayload",
          "[protocol][rename_request_payload]") {
  auto envelope = DecodeFixtureEnvelope("rename/rename-request.json");
  auto rename =
      dovahlink::protocol::DecodeRenameRequestPayload(envelope.payload);
  REQUIRE(rename.has_value());
  CHECK(rename->displayName == "New Name");
}

TEST_CASE("rename_request accepts an empty displayName",
          "[protocol][rename_request_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"displayName": ""})").get_object();
  auto rename = dovahlink::protocol::DecodeRenameRequestPayload(payload);
  REQUIRE(rename.has_value());
  CHECK(rename->displayName.empty());
}

TEST_CASE("rename_request is rejected when displayName is missing",
          "[protocol][rename_request_payload]") {
  boost::json::object payload = boost::json::parse(R"({})").get_object();
  auto rename = dovahlink::protocol::DecodeRenameRequestPayload(payload);
  REQUIRE_FALSE(rename.has_value());
}

TEST_CASE("rename_request is rejected when displayName is null",
          "[protocol][rename_request_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"displayName": null})").get_object();
  auto rename = dovahlink::protocol::DecodeRenameRequestPayload(payload);
  REQUIRE_FALSE(rename.has_value());
}

TEST_CASE("rename_request is rejected when displayName has the wrong type",
          "[protocol][rename_request_payload]") {
  boost::json::object payload =
      boost::json::parse(R"({"displayName": 42})").get_object();
  auto rename = dovahlink::protocol::DecodeRenameRequestPayload(payload);
  REQUIRE_FALSE(rename.has_value());
}

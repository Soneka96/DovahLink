#include "protocol/character_state.hpp"

#include "protocol/fixture_test_support.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

using dovahlink::protocol::test_support::DecodeFixtureEnvelope;

TEST_CASE("character-state-snapshot fixture's data decodes to the expected CharacterState",
          "[protocol][character_state]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-snapshot.json");
    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(snapshot.has_value());

    auto character = dovahlink::protocol::DecodeCharacterState(snapshot->data);
    REQUIRE(character.has_value());
    REQUIRE(character->level.has_value());
    CHECK(*character->level == 12);
    REQUIRE(character->health.has_value());
    CHECK(character->health->current == 180.0);
    CHECK(character->health->maximum == 220.0);
    REQUIRE(character->magicka.has_value());
    REQUIRE(character->stamina.has_value());
}

TEST_CASE("character-state-unavailable fixture decodes with every resource unavailable",
          "[protocol][character_state]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-unavailable.json");
    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(snapshot.has_value());

    auto character = dovahlink::protocol::DecodeCharacterState(snapshot->data);
    REQUIRE(character.has_value());
    CHECK_FALSE(character->level.has_value());
    CHECK_FALSE(character->health.has_value());
    CHECK_FALSE(character->magicka.has_value());
    CHECK_FALSE(character->stamina.has_value());
}

TEST_CASE("character-state-event fixture's data decodes to the expected CharacterState",
          "[protocol][character_state]") {
    auto envelope = DecodeFixtureEnvelope("state/character/character-state-event.json");
    auto event = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(event.has_value());

    auto character = dovahlink::protocol::DecodeCharacterState(event->data);
    REQUIRE(character.has_value());
    REQUIRE(character->level.has_value());
    CHECK(*character->level == 12);
}

TEST_CASE("character state is rejected when a resource is present but not an object",
          "[protocol][character_state]") {
    boost::json::object data =
        boost::json::parse(R"({"level": 1, "health": "not an object", "magicka": null, "stamina": null})").get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("character state is rejected when a resource is missing its maximum field",
          "[protocol][character_state]") {
    boost::json::object data =
        boost::json::parse(R"({"level": 1, "health": {"current": 100.0}, "magicka": null, "stamina": null})")
            .get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("character state is rejected when level is missing", "[protocol][character_state]") {
    boost::json::object data = boost::json::parse(R"({"health": null, "magicka": null, "stamina": null})").get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

TEST_CASE("character state is rejected when level is negative", "[protocol][character_state]") {
    boost::json::object data =
        boost::json::parse(R"({"level": -1, "health": null, "magicka": null, "stamina": null})").get_object();
    auto character = dovahlink::protocol::DecodeCharacterState(data);
    REQUIRE_FALSE(character.has_value());
}

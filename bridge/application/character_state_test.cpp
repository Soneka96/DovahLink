#include "application/character_state.hpp"

#include "protocol/fixture_test_support.hpp"
#include "protocol/messages.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/value.hpp>

#include <optional>

using dovahlink::application::BuildCharacterEventPayload;
using dovahlink::application::BuildCharacterSnapshotPayload;
using dovahlink::application::BuildCharacterStateData;
using dovahlink::application::CharacterSnapshot;
using dovahlink::protocol::DecodeCharacterState;
using dovahlink::protocol::DecodeStateSnapshotPayload;

TEST_CASE("BuildCharacterStateData encodes an available level", "[application][character_state]") {
    CharacterSnapshot snapshot{.level = 12};
    boost::json::object data = BuildCharacterStateData(snapshot);

    REQUIRE(data.if_contains("level"));
    CHECK(data.at("level").as_int64() == 12);
}

TEST_CASE("BuildCharacterStateData encodes an unavailable level as null", "[application][character_state]") {
    CharacterSnapshot snapshot{.level = std::nullopt};
    boost::json::object data = BuildCharacterStateData(snapshot);

    REQUIRE(data.if_contains("level"));
    CHECK(data.at("level").is_null());
}

TEST_CASE("BuildCharacterStateData always publishes health, magicka, and stamina as null in Phase 1",
          "[application][character_state]") {
    CharacterSnapshot snapshot{.level = 12};
    boost::json::object data = BuildCharacterStateData(snapshot);

    REQUIRE(data.if_contains("health"));
    CHECK(data.at("health").is_null());
    REQUIRE(data.if_contains("magicka"));
    CHECK(data.at("magicka").is_null());
    REQUIRE(data.if_contains("stamina"));
    CHECK(data.at("stamina").is_null());
}

TEST_CASE("BuildCharacterSnapshotPayload sets stateArea, revision, and occurredAt",
          "[application][character_state]") {
    CharacterSnapshot snapshot{.level = 12};
    auto payload = BuildCharacterSnapshotPayload(snapshot, 5, "2026-08-11T12:00:00Z");

    CHECK(payload.stateArea == "character");
    CHECK(payload.revision == 5);
    CHECK(payload.occurredAt == "2026-08-11T12:00:00Z");
    REQUIRE(payload.data.if_contains("level"));
    CHECK(payload.data.at("level").as_int64() == 12);
}

TEST_CASE("BuildCharacterEventPayload sets stateArea, baseRevision, and revision",
          "[application][character_state]") {
    CharacterSnapshot snapshot{.level = 13};
    auto payload = BuildCharacterEventPayload(snapshot, 5, 6, "2026-08-11T12:00:05Z");

    CHECK(payload.stateArea == "character");
    CHECK(payload.baseRevision == 5);
    CHECK(payload.revision == 6);
    REQUIRE(payload.data.if_contains("level"));
    CHECK(payload.data.at("level").as_int64() == 13);
}

TEST_CASE("BuildCharacterEventPayload encodes an unavailable level as null",
          "[application][character_state]") {
    CharacterSnapshot snapshot{.level = std::nullopt};
    auto payload = BuildCharacterEventPayload(snapshot, 5, 6, "2026-08-11T12:00:05Z");

    CHECK(payload.occurredAt == "2026-08-11T12:00:05Z");
    REQUIRE(payload.data.if_contains("level"));
    CHECK(payload.data.at("level").is_null());
}

TEST_CASE("encoding then decoding an available level round-trips exactly",
          "[application][character_state]") {
    CharacterSnapshot snapshot{.level = 12};
    boost::json::object data = BuildCharacterStateData(snapshot);

    auto decoded = DecodeCharacterState(data);
    REQUIRE(decoded.has_value());
    REQUIRE(decoded->level.has_value());
    CHECK(*decoded->level == 12);
    CHECK_FALSE(decoded->health.has_value());
    CHECK_FALSE(decoded->magicka.has_value());
    CHECK_FALSE(decoded->stamina.has_value());
}

TEST_CASE("encoding then decoding an unavailable level round-trips exactly",
          "[application][character_state]") {
    CharacterSnapshot snapshot{.level = std::nullopt};
    boost::json::object data = BuildCharacterStateData(snapshot);

    auto decoded = DecodeCharacterState(data);
    REQUIRE(decoded.has_value());
    CHECK_FALSE(decoded->level.has_value());
}

TEST_CASE("a level captured from the real snapshot fixture round-trips through this Phase 1 builder",
          "[application][character_state]") {
    // The character-state-snapshot fixture models a future state where resources
    // are also available (it exists to exercise the Step 11 decoder's full
    // shape); this Phase 1 builder only ever captures level, so this test only
    // carries the fixture's level forward, not its resource values.
    auto envelope = dovahlink::protocol::test_support::DecodeFixtureEnvelope(
        "state/character/character-state-snapshot.json");
    auto fixtureSnapshot = DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(fixtureSnapshot.has_value());
    auto fixtureCharacter = DecodeCharacterState(fixtureSnapshot->data);
    REQUIRE(fixtureCharacter.has_value());
    REQUIRE(fixtureCharacter->level.has_value());

    CharacterSnapshot snapshot{.level = *fixtureCharacter->level};
    auto rebuiltDecoded = DecodeCharacterState(BuildCharacterStateData(snapshot));
    REQUIRE(rebuiltDecoded.has_value());
    CHECK(rebuiltDecoded->level == fixtureCharacter->level);
}

TEST_CASE("a level captured from the real event fixture round-trips through BuildCharacterEventPayload",
          "[application][character_state]") {
    auto envelope = dovahlink::protocol::test_support::DecodeFixtureEnvelope(
        "state/character/character-state-event.json");
    auto fixtureEvent = dovahlink::protocol::DecodeStateEventPayload(envelope.payload);
    REQUIRE(fixtureEvent.has_value());
    auto fixtureCharacter = DecodeCharacterState(fixtureEvent->data);
    REQUIRE(fixtureCharacter.has_value());
    REQUIRE(fixtureCharacter->level.has_value());

    CharacterSnapshot snapshot{.level = *fixtureCharacter->level};
    auto rebuilt = BuildCharacterEventPayload(snapshot, fixtureEvent->baseRevision, fixtureEvent->revision,
                                               fixtureEvent->occurredAt);
    auto rebuiltDecoded = DecodeCharacterState(rebuilt.data);
    REQUIRE(rebuiltDecoded.has_value());
    CHECK(rebuiltDecoded->level == fixtureCharacter->level);
    CHECK(rebuilt.baseRevision == fixtureEvent->baseRevision);
    CHECK(rebuilt.revision == fixtureEvent->revision);
}

TEST_CASE("the real character-state-unavailable fixture's unavailable level round-trips",
          "[application][character_state]") {
    auto envelope = dovahlink::protocol::test_support::DecodeFixtureEnvelope(
        "state/character/character-state-unavailable.json");
    auto fixtureSnapshot = DecodeStateSnapshotPayload(envelope.payload);
    REQUIRE(fixtureSnapshot.has_value());
    auto fixtureCharacter = DecodeCharacterState(fixtureSnapshot->data);
    REQUIRE(fixtureCharacter.has_value());
    CHECK_FALSE(fixtureCharacter->level.has_value());

    CharacterSnapshot snapshot{.level = std::nullopt};
    auto rebuiltDecoded = DecodeCharacterState(BuildCharacterStateData(snapshot));
    REQUIRE(rebuiltDecoded.has_value());
    CHECK_FALSE(rebuiltDecoded->level.has_value());
}

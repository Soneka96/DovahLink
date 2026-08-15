#include "application/character_state_store.hpp"

#include "application/handshake_handler.hpp"
#include "application/revision_tracker.hpp"
#include "application/subscription_handler.hpp"
#include "protocol/messages.hpp"

#include <catch2/catch_test_macros.hpp>

#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>

#include <chrono>
#include <optional>

using dovahlink::application::CharacterStateStore;
using dovahlink::application::HandleSubscribe;
using dovahlink::application::RevisionTracker;
using dovahlink::protocol::Envelope;

TEST_CASE("a fresh CharacterStateStore has no captured level", "[application][character_state_store]") {
    CharacterStateStore store;
    CHECK_FALSE(store.CurrentCharacterSnapshot().level.has_value());
}

TEST_CASE("OnLevelCaptured makes the value available through CurrentCharacterSnapshot",
          "[application][character_state_store]") {
    CharacterStateStore store;
    store.OnLevelCaptured(12);
    auto snapshot = store.CurrentCharacterSnapshot();
    REQUIRE(snapshot.level.has_value());
    CHECK(*snapshot.level == 12);
}

TEST_CASE("a later capture replaces an earlier one", "[application][character_state_store]") {
    CharacterStateStore store;
    store.OnLevelCaptured(10);
    store.OnLevelCaptured(11);
    auto snapshot = store.CurrentCharacterSnapshot();
    REQUIRE(snapshot.level.has_value());
    CHECK(*snapshot.level == 11);
}

TEST_CASE("capturing nullopt after a real value makes the level unavailable again",
          "[application][character_state_store]") {
    CharacterStateStore store;
    store.OnLevelCaptured(15);
    store.OnLevelCaptured(std::nullopt);
    CHECK_FALSE(store.CurrentCharacterSnapshot().level.has_value());
}

TEST_CASE("a CharacterStateStore satisfies HandleSubscribe's CharacterStateProvider seam end to end",
          "[application][character_state_store]") {
    // Proves the store fits the exact seam subscription_handler.cpp consumes,
    // not just its own interface in isolation.
    CharacterStateStore store;
    store.OnLevelCaptured(42);

    RevisionTracker revisions;
    auto now = std::chrono::system_clock::now();
    Envelope subscribeEnvelope{
        .protocolVersion = dovahlink::application::kSupportedProtocolVersion,
        .messageType = "subscribe",
        .messageId = "message-sub-1",
        .sessionId = std::string("session-1"),
        .correlationId = std::nullopt,
        .payload = boost::json::parse(R"({"stateAreas": ["character"]})").get_object(),
    };

    auto result = HandleSubscribe(subscribeEnvelope, "session-1", dovahlink::application::kSupportedProtocolVersion,
                                   store, revisions, now);

    REQUIRE(result.snapshots.size() == 1);
    auto snapshot = dovahlink::protocol::DecodeStateSnapshotPayload(result.snapshots[0].payload);
    REQUIRE(snapshot.has_value());
    auto characterState = dovahlink::protocol::DecodeCharacterState(snapshot->data);
    REQUIRE(characterState.has_value());
    REQUIRE(characterState->level.has_value());
    CHECK(*characterState->level == 42);
}

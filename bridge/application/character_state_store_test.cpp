#include "application/character_state_store.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>

using dovahlink::application::CharacterStateStore;

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

#include "application/character_state_store.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>

using dovahlink::application::CharacterStateStore;
using dovahlink::application::ICharacterStateStore;

TEST_CASE("a fresh CharacterStateStore has no captured level",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    CHECK_FALSE(storeContract.CurrentCharacterSnapshot().level.has_value());
}

TEST_CASE("OnLevelCaptured makes the value available through "
          "CurrentCharacterSnapshot",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    storeContract.OnLevelCaptured(12);
    auto snapshot = storeContract.CurrentCharacterSnapshot();
    REQUIRE(snapshot.level.has_value());
    CHECK(*snapshot.level == 12);
}

TEST_CASE("a later capture replaces an earlier one",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    storeContract.OnLevelCaptured(10);
    storeContract.OnLevelCaptured(11);
    auto snapshot = storeContract.CurrentCharacterSnapshot();
    REQUIRE(snapshot.level.has_value());
    CHECK(*snapshot.level == 11);
}

TEST_CASE(
    "capturing nullopt after a real value makes the level unavailable again",
    "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    storeContract.OnLevelCaptured(15);
    storeContract.OnLevelCaptured(std::nullopt);
    CHECK_FALSE(storeContract.CurrentCharacterSnapshot().level.has_value());
}

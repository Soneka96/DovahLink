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

TEST_CASE("OnLevelCaptured reports true when the first capture is available",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    CHECK(storeContract.OnLevelCaptured(12));
}

TEST_CASE("OnLevelCaptured reports true when the first capture is unavailable",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    CHECK(storeContract.OnLevelCaptured(std::nullopt));
}

TEST_CASE("OnLevelCaptured reports false when a repeated value matches the "
          "stored one",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    CHECK(storeContract.OnLevelCaptured(12));
    CHECK_FALSE(storeContract.OnLevelCaptured(12));
}

TEST_CASE("OnLevelCaptured reports false when a repeated unavailable capture "
          "follows an earlier unavailable capture",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    CHECK(storeContract.OnLevelCaptured(std::nullopt));
    CHECK_FALSE(storeContract.OnLevelCaptured(std::nullopt));
}

TEST_CASE("OnLevelCaptured reports true for an available-to-unavailable "
          "transition",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    storeContract.OnLevelCaptured(12);
    CHECK(storeContract.OnLevelCaptured(std::nullopt));
}

TEST_CASE("OnLevelCaptured reports true for an unavailable-to-available "
          "transition",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    storeContract.OnLevelCaptured(std::nullopt);
    CHECK(storeContract.OnLevelCaptured(12));
}

TEST_CASE("OnLevelCaptured reports true when a different value follows an "
          "earlier one",
          "[application][character_state_store]") {
    CharacterStateStore store;
    ICharacterStateStore& storeContract = store;
    storeContract.OnLevelCaptured(10);
    CHECK(storeContract.OnLevelCaptured(11));
}

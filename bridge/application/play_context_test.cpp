#include "application/play_context.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::application::PlayContext;

TEST_CASE("a fresh PlayContext has the given id and empty character state",
          "[application][play_context]") {
    PlayContext context("ctx-1");

    CHECK(context.id == "ctx-1");
    CHECK_FALSE(context.characterState.CurrentCharacterSnapshot().level);
    CHECK_FALSE(context.revisions.CurrentRevision("character_level"));
}

TEST_CASE("PlayContext owns independent character state and revisions",
          "[application][play_context]") {
    PlayContext first("ctx-1");
    PlayContext second("ctx-2");

    first.characterState.OnLevelCaptured(5);
    first.revisions.StartSnapshot("character_level", "level-5");

    CHECK(first.characterState.CurrentCharacterSnapshot().level == 5);
    CHECK(first.revisions.CurrentRevision("character_level") == 1);
    CHECK_FALSE(second.characterState.CurrentCharacterSnapshot().level);
    CHECK_FALSE(second.revisions.CurrentRevision("character_level"));
}

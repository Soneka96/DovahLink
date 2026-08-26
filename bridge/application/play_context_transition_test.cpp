#include "application/play_context_transition.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::application::PlayContextTransition;

TEST_CASE("PlayContextTransition defaults to no lifecycle effect",
          "[application][play_context_transition]") {
    PlayContextTransition transition;

    CHECK_FALSE(transition.contextInvalidated);
    CHECK_FALSE(transition.newPlayContextId.has_value());
}

TEST_CASE("PlayContextTransition carries invalidation and a new context ID",
          "[application][play_context_transition]") {
    PlayContextTransition transition{
        .contextInvalidated = true,
        .newPlayContextId = std::string("context-1"),
    };

    CHECK(transition.contextInvalidated);
    REQUIRE(transition.newPlayContextId.has_value());
    CHECK(*transition.newPlayContextId == "context-1");
}

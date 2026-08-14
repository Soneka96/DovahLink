#include "application/play_context.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::ApplyLifecycleTransition;
using dovahlink::application::GameLifecycleTracker;
using dovahlink::application::PlayContext;

namespace {
constexpr const char* kCharacter = "character";
}  // namespace

TEST_CASE("AcquireCurrent is nullptr before any context begins", "[application][play_context]") {
    ActivePlayContext active;
    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("Begin makes a context reachable through AcquireCurrent with the given id",
          "[application][play_context]") {
    ActivePlayContext active;
    auto begun = active.Begin("ctx-1");
    REQUIRE(begun);
    CHECK(begun->id == "ctx-1");

    auto acquired = active.AcquireCurrent();
    REQUIRE(acquired);
    CHECK(acquired->id == "ctx-1");
}

TEST_CASE("Reset clears the active context back to nullptr", "[application][play_context]") {
    ActivePlayContext active;
    active.Begin("ctx-1");
    active.Reset();
    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("Reset on an already-empty ActivePlayContext is an idempotent no-op",
          "[application][play_context]") {
    ActivePlayContext active;
    active.Reset();
    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("a context handle acquired before Reset stays valid after the active context is cleared",
          "[application][play_context]") {
    ActivePlayContext active;
    auto acquired = active.Begin("ctx-1");
    active.Reset();

    CHECK_FALSE(active.AcquireCurrent());
    // Reset() only clears ActivePlayContext's own reference; a shared_ptr a
    // caller already holds keeps the context alive and usable.
    CHECK(acquired->id == "ctx-1");
    acquired->revisions.StartSnapshot(kCharacter);
    CHECK(acquired->revisions.CurrentRevision(kCharacter) == 1);
}

TEST_CASE("a fresh PlayContext has the given id and empty character/revision state",
          "[application][play_context]") {
    PlayContext context("ctx-1");
    CHECK(context.id == "ctx-1");
    CHECK_FALSE(context.characterState.CurrentCharacterSnapshot().level.has_value());
    CHECK_FALSE(context.revisions.CurrentRevision(kCharacter).has_value());
}

TEST_CASE("repeated AcquireCurrent calls with no intervening change return the identical instance",
          "[application][play_context]") {
    ActivePlayContext active;
    active.Begin("ctx-1");
    CHECK(active.AcquireCurrent() == active.AcquireCurrent());
}

TEST_CASE("a new Begin discards the previous context's revision counter rather than resetting it "
          "in place",
          "[application][play_context]") {
    ActivePlayContext active;
    auto first = active.Begin("ctx-1");
    first->revisions.StartSnapshot(kCharacter);
    first->revisions.StartSnapshot(kCharacter);
    REQUIRE(first->revisions.CurrentRevision(kCharacter) == 2);

    auto second = active.Begin("ctx-2");
    CHECK(second != first);
    // The new context starts a fresh sequence...
    CHECK(second->revisions.StartSnapshot(kCharacter) == 1);
    // ...while the handle a caller acquired before the swap keeps its own state untouched.
    CHECK(first->revisions.CurrentRevision(kCharacter) == 2);
}

TEST_CASE("ApplyLifecycleTransition resets the active context on invalidation with no new id",
          "[application][play_context]") {
    ActivePlayContext active;
    active.Begin("ctx-1");

    ApplyLifecycleTransition(active, GameLifecycleTracker::Transition{.contextInvalidated = true});

    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("ApplyLifecycleTransition begins a new context when only a new id is present",
          "[application][play_context]") {
    ActivePlayContext active;

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = false, .newPlayContextId = "ctx-1"});

    auto acquired = active.AcquireCurrent();
    REQUIRE(acquired);
    CHECK(acquired->id == "ctx-1");
}

TEST_CASE("ApplyLifecycleTransition resets then begins when both fields are set",
          "[application][play_context]") {
    ActivePlayContext active;
    active.Begin("ctx-1");

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = true, .newPlayContextId = "ctx-2"});

    auto acquired = active.AcquireCurrent();
    REQUIRE(acquired);
    CHECK(acquired->id == "ctx-2");
}

TEST_CASE("ApplyLifecycleTransition's invalidation is idempotent when the active context is "
          "already empty",
          "[application][play_context]") {
    ActivePlayContext active;

    ApplyLifecycleTransition(active, GameLifecycleTracker::Transition{.contextInvalidated = true});

    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("ApplyLifecycleTransition with neither field set leaves the active context untouched",
          "[application][play_context]") {
    ActivePlayContext active;
    auto begun = active.Begin("ctx-1");

    ApplyLifecycleTransition(active, GameLifecycleTracker::Transition{});

    CHECK(active.AcquireCurrent() == begun);
}

#include "application/game_lifecycle_tracker.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>

using dovahlink::application::GameLifecycleTracker;
using dovahlink::application::LifecycleEvent;
using dovahlink::application::LifecycleState;

namespace {

/// Returns a deterministic, strictly increasing generator for assertions
/// that need to distinguish successive play context identifiers.
dovahlink::application::PlayContextIdGenerator SequentialGenerator() {
    return [n = 0]() mutable -> std::optional<std::string> { return "ctx-" + std::to_string(++n); };
}

/// Returns a generator that always fails, matching security::GenerateOpaqueId's
/// documented CNG-failure behavior.
dovahlink::application::PlayContextIdGenerator FailingGenerator() {
    return [] { return std::optional<std::string>{}; };
}

}  // namespace

TEST_CASE("a fresh tracker starts in kNoContext with no active play context",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("Revert from kNoContext is an idempotent no-op", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto transition = tracker.HandleEvent(LifecycleEvent::kRevert);

    CHECK_FALSE(transition.contextInvalidated);
    CHECK_FALSE(transition.newPlayContextId.has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
}

TEST_CASE("PreLoadGame from kNoContext enters kLoading without invalidating anything",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto transition = tracker.HandleEvent(LifecycleEvent::kPreLoadGame);

    CHECK_FALSE(transition.contextInvalidated);
    CHECK(tracker.CurrentState() == LifecycleState::kLoading);
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("PreLoadGame while already kLoading is idempotent", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kPreLoadGame);
    auto transition = tracker.HandleEvent(LifecycleEvent::kPreLoadGame);

    CHECK_FALSE(transition.contextInvalidated);
    CHECK(tracker.CurrentState() == LifecycleState::kLoading);
}

TEST_CASE("PostLoadGameSuccess from kLoading activates a fresh play context",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kPreLoadGame);
    auto transition = tracker.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);

    CHECK_FALSE(transition.contextInvalidated);  // was Loading, not Active -- nothing active to invalidate
    REQUIRE(transition.newPlayContextId.has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kActive);
    CHECK(tracker.CurrentPlayContextId() == transition.newPlayContextId);
}

TEST_CASE("PostLoadGameSuccess activates even without a preceding PreLoadGame",
          "[application][game_lifecycle_tracker]") {
    // Tolerates ordering that public SKSE documentation does not guarantee.
    GameLifecycleTracker tracker(SequentialGenerator());
    auto transition = tracker.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);

    CHECK_FALSE(transition.contextInvalidated);
    REQUIRE(transition.newPlayContextId.has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kActive);
}

TEST_CASE("NewGame from kNoContext activates directly without a Loading detour",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto transition = tracker.HandleEvent(LifecycleEvent::kNewGame);

    CHECK_FALSE(transition.contextInvalidated);
    REQUIRE(transition.newPlayContextId.has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kActive);
}

TEST_CASE("Revert from kActive invalidates the current context", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kNewGame);
    auto transition = tracker.HandleEvent(LifecycleEvent::kRevert);

    CHECK(transition.contextInvalidated);
    CHECK_FALSE(transition.newPlayContextId.has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("double Revert in a row is invalidate-then-idempotent", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kNewGame);

    auto first = tracker.HandleEvent(LifecycleEvent::kRevert);
    auto second = tracker.HandleEvent(LifecycleEvent::kRevert);

    CHECK(first.contextInvalidated);
    CHECK_FALSE(second.contextInvalidated);
}

TEST_CASE("PostLoadGameFailure from kLoading settles into kNoContext without reviving anything",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kNewGame);          // establish an active context
    tracker.HandleEvent(LifecycleEvent::kRevert);            // invalidate it (matches real teardown-before-load)
    tracker.HandleEvent(LifecycleEvent::kPreLoadGame);        // -> kLoading
    auto transition = tracker.HandleEvent(LifecycleEvent::kPostLoadGameFailure);

    CHECK_FALSE(transition.contextInvalidated);  // already invalidated at kPreLoadGame
    CHECK_FALSE(transition.newPlayContextId.has_value());
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("PostLoadGameFailure arriving while unexpectedly kActive still invalidates defensively",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kNewGame);  // kActive, no kPreLoadGame/kRevert in between
    auto transition = tracker.HandleEvent(LifecycleEvent::kPostLoadGameFailure);

    CHECK(transition.contextInvalidated);
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
}

TEST_CASE("reload sequence: Active -> Revert -> PreLoadGame -> Loading -> PostLoadGameSuccess -> new Active",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto first = tracker.HandleEvent(LifecycleEvent::kNewGame);
    tracker.HandleEvent(LifecycleEvent::kRevert);
    tracker.HandleEvent(LifecycleEvent::kPreLoadGame);
    auto second = tracker.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);

    REQUIRE(first.newPlayContextId.has_value());
    REQUIRE(second.newPlayContextId.has_value());
    CHECK(*first.newPlayContextId != *second.newPlayContextId);
    CHECK(tracker.CurrentPlayContextId() == second.newPlayContextId);
}

TEST_CASE("new game while already playing: Active -> Revert -> NewGame -> new Active",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto first = tracker.HandleEvent(LifecycleEvent::kNewGame);
    tracker.HandleEvent(LifecycleEvent::kRevert);
    auto second = tracker.HandleEvent(LifecycleEvent::kNewGame);

    REQUIRE(first.newPlayContextId.has_value());
    REQUIRE(second.newPlayContextId.has_value());
    CHECK(*first.newPlayContextId != *second.newPlayContextId);
}

TEST_CASE("PostLoadGameSuccess arriving while unexpectedly kActive invalidates the old context",
          "[application][game_lifecycle_tracker]") {
    // Out-of-order: no Revert/PreLoadGame in between, exercising Activate()'s
    // hadActiveContext == true branch directly.
    GameLifecycleTracker tracker(SequentialGenerator());
    auto first = tracker.HandleEvent(LifecycleEvent::kNewGame);
    auto second = tracker.HandleEvent(LifecycleEvent::kPostLoadGameSuccess);

    CHECK(second.contextInvalidated);
    REQUIRE(second.newPlayContextId.has_value());
    CHECK(*first.newPlayContextId != *second.newPlayContextId);
}

TEST_CASE("NewGame arriving while unexpectedly kActive invalidates the old context",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto first = tracker.HandleEvent(LifecycleEvent::kNewGame);
    auto second = tracker.HandleEvent(LifecycleEvent::kNewGame);

    CHECK(second.contextInvalidated);
    REQUIRE(second.newPlayContextId.has_value());
    CHECK(*first.newPlayContextId != *second.newPlayContextId);
}

TEST_CASE("PreLoadGame from kActive invalidates the current context", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kNewGame);
    auto transition = tracker.HandleEvent(LifecycleEvent::kPreLoadGame);

    CHECK(transition.contextInvalidated);
    CHECK(tracker.CurrentState() == LifecycleState::kLoading);
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("Revert from kLoading invalidates and returns to kNoContext", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    tracker.HandleEvent(LifecycleEvent::kPreLoadGame);
    auto transition = tracker.HandleEvent(LifecycleEvent::kRevert);

    CHECK(transition.contextInvalidated);
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
}

TEST_CASE("PostLoadGameFailure from kNoContext is a no-op baseline", "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(SequentialGenerator());
    auto transition = tracker.HandleEvent(LifecycleEvent::kPostLoadGameFailure);

    CHECK_FALSE(transition.contextInvalidated);
    CHECK(tracker.CurrentState() == LifecycleState::kNoContext);
}

TEST_CASE("a failing generator clears a previously active context's identifier rather than leaving it stale",
          "[application][game_lifecycle_tracker]") {
    GameLifecycleTracker tracker(FailingGenerator());
    tracker.HandleEvent(LifecycleEvent::kNewGame);  // Activate() with a failing generator: id already nullopt
    tracker.HandleEvent(LifecycleEvent::kRevert);
    auto transition = tracker.HandleEvent(LifecycleEvent::kNewGame);  // Activate() again, still failing

    CHECK(tracker.CurrentState() == LifecycleState::kActive);
    CHECK_FALSE(transition.newPlayContextId.has_value());
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

TEST_CASE("Activate still transitions to kActive when identifier generation fails",
          "[application][game_lifecycle_tracker]") {
    // Matches security::GenerateOpaqueId's documented fallibility: state
    // tracking must not silently fabricate an identifier, but it also must
    // not get stuck refusing to acknowledge the game has moved on.
    GameLifecycleTracker tracker(FailingGenerator());
    auto transition = tracker.HandleEvent(LifecycleEvent::kNewGame);

    CHECK(tracker.CurrentState() == LifecycleState::kActive);
    CHECK_FALSE(transition.newPlayContextId.has_value());
    CHECK_FALSE(tracker.CurrentPlayContextId().has_value());
}

#include "application/active_play_context.hpp"
#include "application/active_play_context_level_sink.hpp"
#include "application/active_play_context_reader.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <memory>
#include <stdexcept>

using dovahlink::application::ActivePlayContext;
using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::ApplyLifecycleTransition;
using dovahlink::application::GameLifecycleTracker;
using dovahlink::application::IActivePlayContext;
using dovahlink::application::IActivePlayContextReader;
using dovahlink::application::PlayContext;
using testing::StrictMock;

namespace {

///  GoogleMock active-play-context contract double.
class MockActivePlayContextReader : public IActivePlayContextReader {
  public:
    MOCK_METHOD(std::shared_ptr<PlayContext>, AcquireCurrent, (),
                (const, override));
};

///  GoogleMock lifecycle-owner contract double for transition application tests.
class MockActivePlayContext : public IActivePlayContext {
  public:
    MOCK_METHOD(std::shared_ptr<PlayContext>, AcquireCurrent, (),
                (const, override));
    MOCK_METHOD(void, Reset, (), (override));
    MOCK_METHOD(std::shared_ptr<PlayContext>, Begin, (std::string), (override));
    MOCK_METHOD(std::shared_ptr<PlayContext>, Replace, (std::string),
                (override));
};

} //  namespace

TEST_CASE("AcquireCurrent is nullptr before any context begins",
          "[application][play_context]") {
    ActivePlayContext active;
    IActivePlayContext& activeContract = active;
    CHECK_FALSE(activeContract.AcquireCurrent());
}

TEST_CASE(
    "Begin makes a context reachable through AcquireCurrent with the given id",
    "[application][play_context]") {
    ActivePlayContext active;
    auto begun = active.Begin("ctx-1");
    REQUIRE(begun);
    CHECK(begun->id == "ctx-1");

    auto acquired = active.AcquireCurrent();
    REQUIRE(acquired);
    CHECK(acquired->id == "ctx-1");
}

TEST_CASE("Reset clears the active context back to nullptr",
          "[application][play_context]") {
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

TEST_CASE("a context handle acquired before Reset stays valid after the active "
          "context is cleared",
          "[application][play_context]") {
    ActivePlayContext active;
    auto acquired = active.Begin("ctx-1");
    active.Reset();

    CHECK_FALSE(active.AcquireCurrent());
    //  Reset() only clears ActivePlayContext's own reference; a shared_ptr a
    //  caller already holds keeps the context alive and usable.
    CHECK(acquired->id == "ctx-1");
    acquired->characterState.OnLevelCaptured(5);
    REQUIRE(
        acquired->characterState.CurrentCharacterSnapshot().level.has_value());
    CHECK(*acquired->characterState.CurrentCharacterSnapshot().level == 5);
}

TEST_CASE("a fresh PlayContext has the given id and empty character state",
          "[application][play_context]") {
    PlayContext context("ctx-1");
    CHECK(context.id == "ctx-1");
    CHECK_FALSE(
        context.characterState.CurrentCharacterSnapshot().level.has_value());
    CHECK_FALSE(context.revisions.CurrentRevision("character_level").has_value());
    CHECK(context.revisions.StartSnapshot("character_level", "level-1") == 1);
}

TEST_CASE("repeated AcquireCurrent calls with no intervening change return the "
          "identical instance",
          "[application][play_context]") {
    ActivePlayContext active;
    active.Begin("ctx-1");
    CHECK(active.AcquireCurrent() == active.AcquireCurrent());
}

TEST_CASE("a new Begin discards the previous context's character state rather "
          "than resetting it "
          "in place",
          "[application][play_context]") {
    ActivePlayContext active;
    auto first = active.Begin("ctx-1");
    first->characterState.OnLevelCaptured(5);
    REQUIRE(first->characterState.CurrentCharacterSnapshot().level == 5);
    REQUIRE(first->revisions.StartSnapshot("character_level", "level-5") == 1);

    auto second = active.Begin("ctx-2");
    CHECK(second != first);
    //  The new context starts with no captured state...
    CHECK_FALSE(
        second->characterState.CurrentCharacterSnapshot().level.has_value());
    CHECK_FALSE(second->revisions.CurrentRevision("character_level").has_value());
    //  ...while the handle a caller acquired before the swap keeps its own state
    //  untouched.
    CHECK(first->characterState.CurrentCharacterSnapshot().level == 5);
    CHECK(first->revisions.CurrentRevision("character_level") == 1);
}

TEST_CASE("Replace swaps the active context without exposing an empty context",
          "[application][play_context]") {
    ActivePlayContext active;
    auto first = active.Begin("ctx-1");
    first->characterState.OnLevelCaptured(5);

    auto second = active.Replace("ctx-2");

    REQUIRE(second);
    CHECK(second->id == "ctx-2");
    CHECK(active.AcquireCurrent() == second);
    CHECK(first != second);
    CHECK_FALSE(second->characterState.CurrentCharacterSnapshot().level);
    CHECK(first->characterState.CurrentCharacterSnapshot().level == 5);
}

TEST_CASE("ApplyLifecycleTransition resets the active context on invalidation "
          "with no new id",
          "[application][play_context]") {
    ActivePlayContext active;
    active.Begin("ctx-1");

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = true});

    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("ApplyLifecycleTransition begins a new context when only a new id is "
          "present",
          "[application][play_context]") {
    ActivePlayContext active;

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = false,
                                                 .newPlayContextId = "ctx-1"});

    auto acquired = active.AcquireCurrent();
    REQUIRE(acquired);
    CHECK(acquired->id == "ctx-1");
}

TEST_CASE(
    "ApplyLifecycleTransition atomically replaces when both fields are set",
    "[application][play_context]") {
    StrictMock<MockActivePlayContext> active;
    auto replacement = std::make_shared<PlayContext>("ctx-2");
    EXPECT_CALL(active, Replace("ctx-2"))
        .WillOnce(testing::Return(replacement));

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = true,
                                                 .newPlayContextId = "ctx-2"});
}

TEST_CASE("ApplyLifecycleTransition preserves the old handle during concrete "
          "replacement",
          "[application][play_context]") {
    ActivePlayContext active;
    auto first = active.Begin("ctx-1");
    first->characterState.OnLevelCaptured(5);

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = true,
                                                 .newPlayContextId = "ctx-2"});

    auto second = active.AcquireCurrent();
    REQUIRE(second);
    CHECK(second->id == "ctx-2");
    CHECK(first != second);
    CHECK(first->characterState.CurrentCharacterSnapshot().level == 5);
    CHECK_FALSE(second->characterState.CurrentCharacterSnapshot().level);
}

TEST_CASE(
    "ApplyLifecycleTransition uses replacement alone when only a new id is set",
    "[application][play_context]") {
    StrictMock<MockActivePlayContext> active;
    auto replacement = std::make_shared<PlayContext>("ctx-1");
    EXPECT_CALL(active, Replace("ctx-1"))
        .WillOnce(testing::Return(replacement));

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.newPlayContextId = "ctx-1"});
}

TEST_CASE("ApplyLifecycleTransition propagates replacement failures",
          "[application][play_context]") {
    StrictMock<MockActivePlayContext> active;
    EXPECT_CALL(active, Replace("ctx-1"))
        .WillOnce(testing::Throw(std::runtime_error("replace failed")));

    CHECK_THROWS_AS(
        ApplyLifecycleTransition(
            active, GameLifecycleTracker::Transition{.newPlayContextId = "ctx-1"}),
        std::runtime_error);
}

TEST_CASE("ApplyLifecycleTransition propagates invalidation failures",
          "[application][play_context]") {
    StrictMock<MockActivePlayContext> active;
    EXPECT_CALL(active, Reset())
        .WillOnce(testing::Throw(std::runtime_error("reset failed")));

    CHECK_THROWS_AS(
        ApplyLifecycleTransition(
            active, GameLifecycleTracker::Transition{.contextInvalidated = true}),
        std::runtime_error);
}

TEST_CASE("ApplyLifecycleTransition's invalidation is idempotent when the "
          "active context is "
          "already empty",
          "[application][play_context]") {
    ActivePlayContext active;

    ApplyLifecycleTransition(
        active, GameLifecycleTracker::Transition{.contextInvalidated = true});

    CHECK_FALSE(active.AcquireCurrent());
}

TEST_CASE("ApplyLifecycleTransition with neither field set leaves the active "
          "context untouched",
          "[application][play_context]") {
    ActivePlayContext active;
    auto begun = active.Begin("ctx-1");

    ApplyLifecycleTransition(active, GameLifecycleTracker::Transition{});

    CHECK(active.AcquireCurrent() == begun);
}

TEST_CASE("ActivePlayContextLevelSink forwards through its context contract",
          "[application][play_context]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    auto context = std::make_shared<PlayContext>("ctx-1");
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(context));
    ActivePlayContextLevelSink sink(activeContext);

    sink.OnLevelCaptured(12);

    REQUIRE(context->characterState.CurrentCharacterSnapshot().level.has_value());
    CHECK(*context->characterState.CurrentCharacterSnapshot().level == 12);
}

TEST_CASE("ActivePlayContextLevelSink drops captures from an empty context contract",
          "[application][play_context]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}));
    ActivePlayContextLevelSink sink(activeContext);

    sink.OnLevelCaptured(12);
}

TEST_CASE("ActivePlayContextLevelSink forwards an unavailable level",
          "[application][play_context]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    auto context = std::make_shared<PlayContext>("ctx-1");
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(context));
    ActivePlayContextLevelSink sink(activeContext);

    sink.OnLevelCaptured(std::nullopt);

    CHECK_FALSE(context->characterState.CurrentCharacterSnapshot().level);
}

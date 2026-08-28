#include "application/active_play_context_level_sink.hpp"
#include "application/active_play_context_provider.hpp"
#include "application/application_test_support.hpp"
#include "application/capture_work_item.hpp"
#include "application/play_context_lifecycle.hpp"
#include "game_state/game_state_test_support.hpp"
#include "game_state/level_increase_handler.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <cstdint>
#include <memory>
#include <optional>
#include <string>

using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::ActivePlayContextProvider;
using dovahlink::application::CaptureWorkItem;
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::PlayContext;
using dovahlink::application::PlayContextLifecycle;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::MockActivePlayContextLevelSink;
using dovahlink::application::test_support::MockCaptureDispatchWorker;
using dovahlink::application::test_support::MockRegisteredStateAreaPolicy;
using dovahlink::game_state::ILevelIncreaseHandler;
using dovahlink::game_state::IPlayerLevelAccessor;
using dovahlink::game_state::LevelIncreaseHandler;
using dovahlink::game_state::test_support::MockPlayerLevelAccessor;
using testing::StrictMock;

TEST_CASE("HandleLevelIncrease pushes the accessor's current level to the "
          "context BeginCapture pinned",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(sink, BeginCapture()).WillOnce(testing::Return(context));
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{15}));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{15}));
    LevelIncreaseHandler handler(accessor, sink);
    ILevelIncreaseHandler& handlerContract = handler;

    handlerContract.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease pushes nullopt when the accessor has no "
          "trustworthy level",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(sink, BeginCapture()).WillOnce(testing::Return(context));
    EXPECT_CALL(accessor, ReadLevel()).WillOnce(testing::Return(std::nullopt));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease re-reads the accessor on every call rather than "
          "caching",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(sink, BeginCapture())
        .Times(3)
        .WillRepeatedly(testing::Return(context));
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{10}))
        .WillOnce(testing::Return(std::optional<std::int64_t>{11}))
        .WillOnce(testing::Return(std::nullopt));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{10}));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{11}));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
    handler.HandleLevelIncrease();
    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease routes an untrustworthy raw value through "
          "CaptureLevel's rules",
          "[game_state][level_increase_handler]") {
    //  Zero is CaptureLevel's own "untrustworthy" sentinel (level_adapter.hpp);
    //  proving the handler goes through CaptureLevel rather than forwarding
    //  the accessor's raw value directly.
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(sink, BeginCapture()).WillOnce(testing::Return(context));
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{0}));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease does not read the accessor when no capture "
          "target is pinned",
          "[game_state][level_increase_handler]") {
    //  StrictMock<MockPlayerLevelAccessor> with no ReadLevel() expectation
    //  fails the test if the accessor is read at all -- proving BeginCapture
    //  runs before the runtime read, not only before OnLevelCaptured.
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    EXPECT_CALL(sink, BeginCapture())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease resumes reading the accessor once a capture "
          "target is pinned again",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(sink, BeginCapture())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}))
        .WillOnce(testing::Return(context));
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{20}));
    EXPECT_CALL(sink, OnLevelCaptured(context, std::optional<std::int64_t>{20}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease never re-reads the active context after "
          "BeginCapture pins it",
          "[game_state][level_increase_handler]") {
    //  A real ActivePlayContextLevelSink over a strict-mock provider:
    //  CurrentPlayContext()'s default cardinality is exactly once, so a
    //  second internal query from OnLevelCaptured would fail this test --
    //  proving the guard-and-pin snapshot survives through the real sink,
    //  not only through a hand-written mock sink.
    StrictMock<dovahlink::application::test_support::MockActivePlayContextProvider>
        activeContext;
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    auto context = BuildPlayContext("context-1");
    EXPECT_CALL(activeContext, CurrentPlayContext())
        .WillOnce(testing::Return(context));
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(testing::_))
        .WillOnce(testing::Return(false));
    ActivePlayContextLevelSink levelSink(activeContext, registeredAreaPolicy,
                                         captureWorker, "test_state_area");
    StrictMock<MockPlayerLevelAccessor> accessor;
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{15}));
    LevelIncreaseHandler handler(accessor, levelSink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease composes with the active play-context writer",
          "[game_state][level_increase_handler]") {
    auto context = BuildPlayContext("context-1");
    PlayContextLifecycle lifecycle(
        [] { return std::optional<std::string>("context-1"); },
        [context](std::string) { return context; });
    lifecycle.HandleEvent(dovahlink::application::LifecycleEvent::kNewGame);
    ActivePlayContextProvider activeContext(lifecycle);
    StrictMock<MockRegisteredStateAreaPolicy> registeredAreaPolicy;
    StrictMock<MockCaptureDispatchWorker> captureWorker;
    EXPECT_CALL(registeredAreaPolicy, IsRegistered(testing::_))
        .WillOnce(testing::Return(true));
    CaptureWorkItem capturedItem;
    EXPECT_CALL(captureWorker, TryEnqueue(testing::_))
        .WillOnce(testing::DoAll(testing::SaveArg<0>(&capturedItem),
                                 testing::Return(true)));
    ActivePlayContextLevelSink levelSink(activeContext, registeredAreaPolicy,
                                         captureWorker, "test_state_area");
    StrictMock<MockPlayerLevelAccessor> accessor;
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{15}));

    LevelIncreaseHandler handler(accessor, levelSink);
    ILevelIncreaseHandler& handlerContract = handler;
    handlerContract.HandleLevelIncrease();

    //  The worker owns applying a captured value to the context, per Stage
    //  5's per-state-area ordering point; invoking the saved closure here
    //  stands in for that worker-thread dispatch and proves the whole
    //  real composition -- handler, sink, and lifecycle -- produces an item
    //  that actually updates the pinned context when applied.
    REQUIRE(capturedItem.playContext == context);
    REQUIRE(capturedItem.applyAndBuildIfChanged);
    capturedItem.applyAndBuildIfChanged(*context);
    CHECK(context->characterState.CurrentCharacterSnapshot().level == 15);
}

#include "application/active_play_context_level_sink.hpp"
#include "application/application_test_support.hpp"
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
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::PlayContext;
using dovahlink::application::PlayContextLifecycle;
using dovahlink::application::test_support::BuildPlayContext;
using dovahlink::application::test_support::MockActivePlayContextLevelSink;
using dovahlink::game_state::ILevelIncreaseHandler;
using dovahlink::game_state::IPlayerLevelAccessor;
using dovahlink::game_state::LevelIncreaseHandler;
using dovahlink::game_state::test_support::MockPlayerLevelAccessor;
using testing::StrictMock;

TEST_CASE("HandleLevelIncrease pushes the accessor's current level to the sink",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{15}));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{15}));
    LevelIncreaseHandler handler(accessor, sink);
    ILevelIncreaseHandler& handlerContract = handler;

    handlerContract.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease pushes nullopt when the accessor has no "
          "trustworthy level",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    EXPECT_CALL(accessor, ReadLevel()).WillOnce(testing::Return(std::nullopt));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease re-reads the accessor on every call rather than "
          "caching",
          "[game_state][level_increase_handler]") {
    StrictMock<MockPlayerLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{10}))
        .WillOnce(testing::Return(std::optional<std::int64_t>{11}))
        .WillOnce(testing::Return(std::nullopt));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{10}));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{11}));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{}));
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
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{0}));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease composes with the active play-context writer",
          "[game_state][level_increase_handler]") {
    auto context = BuildPlayContext("context-1");
    PlayContextLifecycle lifecycle(
        [] { return std::optional<std::string>("context-1"); },
        [context](std::string) { return context; });
    lifecycle.HandleEvent(dovahlink::application::LifecycleEvent::kNewGame);
    ActivePlayContextLevelSink levelSink(lifecycle);
    StrictMock<MockPlayerLevelAccessor> accessor;
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{15}));

    LevelIncreaseHandler handler(accessor, levelSink);
    ILevelIncreaseHandler& handlerContract = handler;
    handlerContract.HandleLevelIncrease();

    CHECK(context->characterState.CurrentCharacterSnapshot().level == 15);
}

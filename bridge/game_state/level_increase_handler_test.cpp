#include "game_state/level_increase_handler.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <cstdint>
#include <optional>

using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::game_state::ILevelAccessor;
using dovahlink::game_state::ILevelIncreaseHandler;
using dovahlink::game_state::LevelIncreaseHandler;
using testing::StrictMock;

namespace {

///  GoogleMock level-accessor contract double.
class MockLevelAccessor : public ILevelAccessor {
  public:
    MOCK_METHOD(std::optional<std::int64_t>, ReadLevel, (), (const, override));
};

///  GoogleMock active-play-context level-sink contract double.
class MockActivePlayContextLevelSink : public IActivePlayContextLevelSink {
  public:
    MOCK_METHOD(void, OnLevelCaptured, (std::optional<std::int64_t>),
                (override));
};

} //  namespace

TEST_CASE("HandleLevelIncrease pushes the accessor's current level to the sink",
          "[game_state][level_increase_handler]") {
    StrictMock<MockLevelAccessor> accessor;
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
    StrictMock<MockLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    EXPECT_CALL(accessor, ReadLevel()).WillOnce(testing::Return(std::nullopt));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

TEST_CASE("HandleLevelIncrease re-reads the accessor on every call rather than "
          "caching",
          "[game_state][level_increase_handler]") {
    StrictMock<MockLevelAccessor> accessor;
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
    StrictMock<MockLevelAccessor> accessor;
    StrictMock<MockActivePlayContextLevelSink> sink;
    EXPECT_CALL(accessor, ReadLevel())
        .WillOnce(testing::Return(std::optional<std::int64_t>{0}));
    EXPECT_CALL(sink, OnLevelCaptured(std::optional<std::int64_t>{}));
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
}

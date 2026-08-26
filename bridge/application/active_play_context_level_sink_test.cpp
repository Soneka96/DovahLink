#include "application/active_play_context_level_sink.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <memory>
#include <optional>

using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::IActivePlayContextReader;
using dovahlink::application::PlayContext;
using testing::StrictMock;

namespace {

///  GoogleMock active-play-context reader contract double.
class MockActivePlayContextReader : public IActivePlayContextReader {
  public:
    MOCK_METHOD(std::shared_ptr<PlayContext>, AcquireCurrent, (),
                (const, override));
};

} //  namespace

TEST_CASE("ActivePlayContextLevelSink forwards a captured level",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    auto context = std::make_shared<PlayContext>("ctx-1");
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(context));
    ActivePlayContextLevelSink sink(activeContext);
    IActivePlayContextLevelSink& sinkContract = sink;

    sinkContract.OnLevelCaptured(12);

    REQUIRE(context->characterState.CurrentCharacterSnapshot().level.has_value());
    CHECK(*context->characterState.CurrentCharacterSnapshot().level == 12);
}

TEST_CASE("ActivePlayContextLevelSink drops a capture with no active context",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(std::shared_ptr<PlayContext>{}));
    ActivePlayContextLevelSink sink(activeContext);

    sink.OnLevelCaptured(12);
}

TEST_CASE("ActivePlayContextLevelSink forwards an unavailable level",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    auto context = std::make_shared<PlayContext>("ctx-1");
    context->characterState.OnLevelCaptured(12);
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(context));
    ActivePlayContextLevelSink sink(activeContext);

    sink.OnLevelCaptured(std::nullopt);

    CHECK_FALSE(context->characterState.CurrentCharacterSnapshot().level);
}

TEST_CASE("ActivePlayContextLevelSink reads the current context for each capture",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockActivePlayContextReader> activeContext;
    auto first = std::make_shared<PlayContext>("ctx-1");
    auto second = std::make_shared<PlayContext>("ctx-2");
    EXPECT_CALL(activeContext, AcquireCurrent())
        .WillOnce(testing::Return(first))
        .WillOnce(testing::Return(second));
    ActivePlayContextLevelSink sink(activeContext);

    sink.OnLevelCaptured(7);
    sink.OnLevelCaptured(8);

    CHECK(first->characterState.CurrentCharacterSnapshot().level == 7);
    CHECK(second->characterState.CurrentCharacterSnapshot().level == 8);
}

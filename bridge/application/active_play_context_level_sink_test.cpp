#include "application/active_play_context_level_sink.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <cstdint>
#include <optional>
#include <string>

using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::test_support::MockPlayContextLifecycle;
using testing::StrictMock;

TEST_CASE("ActivePlayContextLevelSink forwards a captured level",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{12}));
    ActivePlayContextLevelSink sink(lifecycle);
    IActivePlayContextLevelSink& sinkContract = sink;

    sinkContract.OnLevelCaptured(12);
}

TEST_CASE("ActivePlayContextLevelSink forwards an unavailable level",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    EXPECT_CALL(lifecycle, CaptureLevel(std::optional<std::int64_t>{}));
    ActivePlayContextLevelSink sink(lifecycle);

    sink.OnLevelCaptured(std::nullopt);
}

TEST_CASE("ActivePlayContextLevelSink's IsCaptureActive forwards the "
          "lifecycle's active state",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    EXPECT_CALL(lifecycle, CurrentState())
        .WillOnce(testing::Return(dovahlink::application::LifecycleState::kActive));
    ActivePlayContextLevelSink sink(lifecycle);
    IActivePlayContextLevelSink& sinkContract = sink;

    CHECK(sinkContract.IsCaptureActive());
}

TEST_CASE("ActivePlayContextLevelSink's IsCaptureActive reports inactive "
          "while loading",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    EXPECT_CALL(lifecycle, CurrentState())
        .WillOnce(testing::Return(dovahlink::application::LifecycleState::kLoading));
    ActivePlayContextLevelSink sink(lifecycle);

    CHECK_FALSE(sink.IsCaptureActive());
}

TEST_CASE("ActivePlayContextLevelSink's IsCaptureActive reports inactive "
          "with no play context",
          "[application][active_play_context_level_sink]") {
    StrictMock<MockPlayContextLifecycle> lifecycle;
    EXPECT_CALL(lifecycle, CurrentState())
        .WillOnce(testing::Return(dovahlink::application::LifecycleState::kNoContext));
    ActivePlayContextLevelSink sink(lifecycle);

    CHECK_FALSE(sink.IsCaptureActive());
}

#include "application/active_play_context_level_sink.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <cstdint>
#include <optional>
#include <string>

using dovahlink::application::ActivePlayContextLevelSink;
using dovahlink::application::IActivePlayContextLevelSink;
using dovahlink::application::IPlayContextLifecycle;
using dovahlink::application::LifecycleEvent;
using dovahlink::application::LifecycleState;
using testing::StrictMock;

namespace {

///  GoogleMock lifecycle aggregate double for the level-writing adapter.
class MockPlayContextLifecycle : public IPlayContextLifecycle {
  public:
    MOCK_METHOD(IPlayContextLifecycle::Transition, HandleEvent,
                (LifecycleEvent), (override));
    MOCK_METHOD(std::optional<std::string>, CurrentPlayContextId, (),
                (const, override));
    MOCK_METHOD(LifecycleState, CurrentState, (), (const, override));
    MOCK_METHOD(void, CaptureLevel, (std::optional<std::int64_t>), (override));
};

} //  namespace

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

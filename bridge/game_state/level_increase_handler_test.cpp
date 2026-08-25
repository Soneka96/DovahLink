#include "game_state/level_increase_handler.hpp"

#include <catch2/catch_test_macros.hpp>
#include <fakeit.hpp>

#include <cstdint>
#include <optional>

using dovahlink::application::LevelEventSink;
using dovahlink::game_state::LevelAccessor;
using dovahlink::game_state::LevelIncreaseHandler;
using fakeit::Mock;
using fakeit::Verify;
using fakeit::VerifyNoOtherInvocations;
using fakeit::When;

TEST_CASE("HandleLevelIncrease pushes the accessor's current level to the sink",
          "[game_state][level_increase_handler]") {
    Mock<LevelAccessor> accessor;
    Mock<LevelEventSink> sink;
    When(Method(accessor, ReadLevel)).Return(std::optional<std::int64_t>{15});
    When(Method(sink, OnLevelCaptured))
        .AlwaysDo([](const std::optional<std::int64_t>&) {});
    LevelIncreaseHandler handler(accessor.get(), sink.get());

    handler.HandleLevelIncrease();

    Verify(Method(accessor, ReadLevel) +
           Method(sink, OnLevelCaptured).Using(std::optional<std::int64_t>{15}));
    VerifyNoOtherInvocations(accessor, sink);
}

TEST_CASE("HandleLevelIncrease pushes nullopt when the accessor has no "
          "trustworthy level",
          "[game_state][level_increase_handler]") {
    Mock<LevelAccessor> accessor;
    Mock<LevelEventSink> sink;
    When(Method(accessor, ReadLevel)).Return(std::nullopt);
    When(Method(sink, OnLevelCaptured)).Return();
    LevelIncreaseHandler handler(accessor.get(), sink.get());

    handler.HandleLevelIncrease();

    Verify(Method(accessor, ReadLevel)).Once();
    Verify(Method(sink, OnLevelCaptured).Using(std::optional<std::int64_t>{}))
        .Once();
    VerifyNoOtherInvocations(accessor, sink);
}

TEST_CASE("HandleLevelIncrease re-reads the accessor on every call rather than "
          "caching",
          "[game_state][level_increase_handler]") {
    Mock<LevelAccessor> accessor;
    Mock<LevelEventSink> sink;
    When(Method(accessor, ReadLevel))
        .Return(std::optional<std::int64_t>{10}, std::optional<std::int64_t>{11},
                std::nullopt);
    When(Method(sink, OnLevelCaptured))
        .AlwaysDo([](const std::optional<std::int64_t>&) {});
    LevelIncreaseHandler handler(accessor.get(), sink.get());

    handler.HandleLevelIncrease();
    handler.HandleLevelIncrease();
    handler.HandleLevelIncrease();

    Verify(Method(accessor, ReadLevel)).Exactly(3);
    Verify(Method(sink, OnLevelCaptured).Using(std::optional<std::int64_t>{10}))
        .Once();
    Verify(Method(sink, OnLevelCaptured).Using(std::optional<std::int64_t>{11}))
        .Once();
    Verify(Method(sink, OnLevelCaptured).Using(std::optional<std::int64_t>{}))
        .Once();
    VerifyNoOtherInvocations(accessor, sink);
}

TEST_CASE("HandleLevelIncrease routes an untrustworthy raw value through "
          "CaptureLevel's rules",
          "[game_state][level_increase_handler]") {
    //  Zero is CaptureLevel's own "untrustworthy" sentinel (level_adapter.hpp);
    //  proving the handler goes through CaptureLevel rather than forwarding
    //  the accessor's raw value directly.
    Mock<LevelAccessor> accessor;
    Mock<LevelEventSink> sink;
    When(Method(accessor, ReadLevel)).Return(std::optional<std::int64_t>{0});
    When(Method(sink, OnLevelCaptured)).Return();
    LevelIncreaseHandler handler(accessor.get(), sink.get());

    handler.HandleLevelIncrease();

    Verify(Method(accessor, ReadLevel)).Once();
    Verify(Method(sink, OnLevelCaptured).Using(std::optional<std::int64_t>{}))
        .Once();
    VerifyNoOtherInvocations(accessor, sink);
}

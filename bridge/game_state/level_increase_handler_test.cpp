#include "game_state/level_increase_handler.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <optional>
#include <vector>

using dovahlink::application::LevelEventSink;
using dovahlink::game_state::LevelAccessor;
using dovahlink::game_state::LevelIncreaseHandler;

namespace {

class FakeLevelAccessor : public LevelAccessor {
public:
    explicit FakeLevelAccessor(std::optional<std::int64_t> value) : value_(value) {}

    [[nodiscard]] std::optional<std::int64_t> ReadLevel() const override { return value_; }

    void SetValue(std::optional<std::int64_t> value) { value_ = value; }

private:
    std::optional<std::int64_t> value_;
};

// Records only what LevelIncreaseHandler actually hands it: an owned
// std::optional<int64_t>. There is no member here that could accept a
// RE::LevelIncrease::Event pointer or a PlayerCharacter*, so a regression
// that tried to forward the raw event through this seam would fail to
// compile, not silently pass.
class FakeLevelEventSink : public LevelEventSink {
public:
    void OnLevelCaptured(std::optional<std::int64_t> level) override { received.push_back(level); }

    std::vector<std::optional<std::int64_t>> received;
};

}  // namespace

TEST_CASE("HandleLevelIncrease pushes the accessor's current level to the sink",
          "[game_state][level_increase_handler]") {
    FakeLevelAccessor accessor(15);
    FakeLevelEventSink sink;
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();

    REQUIRE(sink.received.size() == 1);
    REQUIRE(sink.received[0].has_value());
    CHECK(*sink.received[0] == 15);
}

TEST_CASE("HandleLevelIncrease pushes nullopt when the accessor has no trustworthy level",
          "[game_state][level_increase_handler]") {
    FakeLevelAccessor accessor(std::nullopt);
    FakeLevelEventSink sink;
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();

    REQUIRE(sink.received.size() == 1);
    CHECK_FALSE(sink.received[0].has_value());
}

TEST_CASE("HandleLevelIncrease re-reads the accessor on every call rather than caching",
          "[game_state][level_increase_handler]") {
    FakeLevelAccessor accessor(10);
    FakeLevelEventSink sink;
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();
    accessor.SetValue(11);
    handler.HandleLevelIncrease();
    accessor.SetValue(std::nullopt);
    handler.HandleLevelIncrease();

    REQUIRE(sink.received.size() == 3);
    CHECK(*sink.received[0] == 10);
    CHECK(*sink.received[1] == 11);
    CHECK_FALSE(sink.received[2].has_value());
}

TEST_CASE("HandleLevelIncrease routes an untrustworthy raw value through CaptureLevel's rules",
          "[game_state][level_increase_handler]") {
    // Zero is CaptureLevel's own "untrustworthy" sentinel (level_adapter.hpp);
    // proving the handler goes through CaptureLevel rather than forwarding
    // the accessor's raw value directly.
    FakeLevelAccessor accessor(0);
    FakeLevelEventSink sink;
    LevelIncreaseHandler handler(accessor, sink);

    handler.HandleLevelIncrease();

    REQUIRE(sink.received.size() == 1);
    CHECK_FALSE(sink.received[0].has_value());
}

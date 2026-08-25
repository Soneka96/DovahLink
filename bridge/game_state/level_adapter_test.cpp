#include "game_state/level_adapter.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <optional>

using dovahlink::game_state::CaptureLevel;
using dovahlink::game_state::ILevelAccessor;

namespace {

///  Returns the configured raw level for adapter tests.
class FakeLevelAccessor : public ILevelAccessor {
  public:
    ///  Stores the raw level value returned by ReadLevel.
    explicit FakeLevelAccessor(std::optional<std::int64_t> value)
        : value_(value) {}

    ///  @copydoc ILevelAccessor::ReadLevel
    [[nodiscard]] std::optional<std::int64_t> ReadLevel() const override {
        return value_;
    }

  private:
    ///  Raw level value, including the unavailable state.
    std::optional<std::int64_t> value_;
};

} //  namespace

TEST_CASE("CaptureLevel returns the raw value for a positive level",
          "[game_state][level_adapter]") {
    FakeLevelAccessor accessor(12);
    auto level = CaptureLevel(accessor);
    REQUIRE(level.has_value());
    CHECK(*level == 12);
}

TEST_CASE("CaptureLevel returns nullopt when no player object is available",
          "[game_state][level_adapter]") {
    FakeLevelAccessor accessor(std::nullopt);
    CHECK_FALSE(CaptureLevel(accessor).has_value());
}

TEST_CASE("CaptureLevel treats a raw level of zero as untrustworthy, not a "
          "real value",
          "[game_state][level_adapter]") {
    FakeLevelAccessor accessor(0);
    CHECK_FALSE(CaptureLevel(accessor).has_value());
}

TEST_CASE("CaptureLevel treats a negative raw level as untrustworthy",
          "[game_state][level_adapter]") {
    FakeLevelAccessor accessor(-1);
    CHECK_FALSE(CaptureLevel(accessor).has_value());
}

TEST_CASE(
    "CaptureLevel returns level 1, the minimum real Skyrim level, unchanged",
    "[game_state][level_adapter]") {
    FakeLevelAccessor accessor(1);
    auto level = CaptureLevel(accessor);
    REQUIRE(level.has_value());
    CHECK(*level == 1);
}

TEST_CASE("CaptureLevel returns a high level unchanged",
          "[game_state][level_adapter]") {
    //  Skyrim has no hard level cap in modded play; this is just a
    //  representative large value, not a claimed maximum.
    FakeLevelAccessor accessor(1000);
    auto level = CaptureLevel(accessor);
    REQUIRE(level.has_value());
    CHECK(*level == 1000);
}

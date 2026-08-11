#include "application/level_event_sink.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <optional>
#include <vector>

namespace {

// A minimal test double proving LevelEventSink is a pure push seam: it can
// only ever receive values through OnLevelCaptured, since that is the only
// member the interface exposes. There is no "current level" accessor to
// poll -- calling something like fake.GetLevel() would simply not compile,
// which is the strongest guarantee this design can offer.
class FakeLevelEventSink : public dovahlink::application::LevelEventSink {
public:
    void OnLevelCaptured(std::optional<std::int64_t> level) override { received.push_back(level); }

    std::vector<std::optional<std::int64_t>> received;
};

}  // namespace

TEST_CASE("OnLevelCaptured delivers a pushed level value", "[application][level_event_sink]") {
    FakeLevelEventSink sink;
    sink.OnLevelCaptured(12);

    REQUIRE(sink.received.size() == 1);
    REQUIRE(sink.received[0].has_value());
    CHECK(*sink.received[0] == 12);
}

TEST_CASE("OnLevelCaptured delivers an unavailable level as nullopt", "[application][level_event_sink]") {
    FakeLevelEventSink sink;
    sink.OnLevelCaptured(std::nullopt);

    REQUIRE(sink.received.size() == 1);
    CHECK_FALSE(sink.received[0].has_value());
}

TEST_CASE("multiple pushed captures are delivered in order", "[application][level_event_sink]") {
    FakeLevelEventSink sink;
    sink.OnLevelCaptured(10);
    sink.OnLevelCaptured(11);
    sink.OnLevelCaptured(std::nullopt);
    sink.OnLevelCaptured(12);

    REQUIRE(sink.received.size() == 4);
    CHECK(*sink.received[0] == 10);
    CHECK(*sink.received[1] == 11);
    CHECK_FALSE(sink.received[2].has_value());
    CHECK(*sink.received[3] == 12);
}

TEST_CASE("a fresh sink has received nothing until something is pushed to it",
          "[application][level_event_sink]") {
    // There is no accessor to query a "current" level independent of a push;
    // the only observable state is what has actually been pushed so far.
    FakeLevelEventSink sink;
    CHECK(sink.received.empty());
}

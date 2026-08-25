#include "application/level_event_sink.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <optional>
#include <vector>

namespace {

///  Captures pushed level values without exposing a polling accessor.
class FakeLevelEventSink : public dovahlink::application::ILevelEventSink {
  public:
    ///  @copydoc dovahlink::application::ILevelEventSink::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override {
        received.push_back(level);
    }

    ///  Values received from the production push seam, in arrival order.
    std::vector<std::optional<std::int64_t>> received;
};

} //  namespace

TEST_CASE("OnLevelCaptured delivers a pushed level value",
          "[application][level_event_sink]") {
    FakeLevelEventSink sink;
    sink.OnLevelCaptured(12);

    REQUIRE(sink.received.size() == 1);
    REQUIRE(sink.received[0].has_value());
    CHECK(*sink.received[0] == 12);
}

TEST_CASE("OnLevelCaptured delivers an unavailable level as nullopt",
          "[application][level_event_sink]") {
    FakeLevelEventSink sink;
    sink.OnLevelCaptured(std::nullopt);

    REQUIRE(sink.received.size() == 1);
    CHECK_FALSE(sink.received[0].has_value());
}

TEST_CASE("multiple pushed captures are delivered in order",
          "[application][level_event_sink]") {
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
    //  There is no accessor to query a "current" level independent of a push;
    //  the only observable state is what has actually been pushed so far.
    FakeLevelEventSink sink;
    CHECK(sink.received.empty());
}

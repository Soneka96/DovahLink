#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads the runtime level sink source for structural assertions.
std::string ReadLevelSinkSource() {
    std::ifstream file(DOVAHLINK_LEVEL_INCREASE_SINK_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

///  Reads the runtime level sink header for structural assertions.
std::string ReadLevelSinkHeader() {
    std::ifstream file(DOVAHLINK_LEVEL_INCREASE_SINK_HEADER_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  The test target intentionally does not link CommonLibSSE-NG. This structural
//  test protects the runtime adapter's contract and callback boundary without
//  pretending to test Skyrim internals as DovahLink behavior.
TEST_CASE("CommonLibLevelIncreaseSink keeps its contract and callback boundary",
          "[game_state][level_increase_sink]") {
    const std::string header = ReadLevelSinkHeader();
    const std::string source = ReadLevelSinkSource();

    CHECK(header.find("class ICommonLibLevelIncreaseSink") !=
          std::string::npos);
    CHECK(header.find("public ICommonLibLevelIncreaseSink") !=
          std::string::npos);
    CHECK(header.find("ILevelIncreaseHandler& handler_") !=
          std::string::npos);
    CHECK(source.find("if (callbackRunner_)") != std::string::npos);
    CHECK(source.find("handler_.HandleLevelIncrease();") !=
          std::string::npos);
    CHECK(source.find("return RE::BSEventNotifyControl::kContinue;") !=
          std::string::npos);
    CHECK(source.find("if (!registered_)") != std::string::npos);
    CHECK(source.find("RemoveEventSink(this);") != std::string::npos);

    //  Capture timing (ai/context/skse/architecture.md's "Threading and
    //  callbacks": capture must be bounded and non-blocking) is measured
    //  around the synchronous HandleLevelIncrease call and logged, not just
    //  invoked.
    std::size_t captureStartPos = source.find("captureStart =");
    std::size_t handleCallPos = source.find("handler_.HandleLevelIncrease();");
    std::size_t captureDurationPos = source.find("captureDuration =");
    std::size_t logPos = source.find("SKSE::log::info(");
    REQUIRE(captureStartPos != std::string::npos);
    REQUIRE(handleCallPos != std::string::npos);
    REQUIRE(captureDurationPos != std::string::npos);
    REQUIRE(logPos != std::string::npos);
    CHECK(captureStartPos < handleCallPos);
    CHECK(handleCallPos < captureDurationPos);
    CHECK(captureDurationPos < logPos);
    CHECK(source.find("\"[capture key=level] duration_us={}\"") !=
          std::string::npos);

    //  Elapsed time uses the monotonic clock (immune to wall-clock
    //  adjustments) rather than a wall-clock or high-resolution clock, and
    //  the logged value is a microsecond count actually derived from
    //  captureDuration rather than an unrelated value.
    std::size_t steadyClockCount = 0;
    std::size_t steadyClockPos = 0;
    while ((steadyClockPos =
                source.find("std::chrono::steady_clock::now()", steadyClockPos)) !=
           std::string::npos) {
        ++steadyClockCount;
        steadyClockPos += 1;
    }
    CHECK(steadyClockCount == 2);
    CHECK(dovahlink::test_support::ContainsSourceText(
        source,
        "std::chrono::duration_cast<std::chrono::microseconds>("
        "captureDuration)"));
}

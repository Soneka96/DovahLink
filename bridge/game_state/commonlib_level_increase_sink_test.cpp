#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

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
}

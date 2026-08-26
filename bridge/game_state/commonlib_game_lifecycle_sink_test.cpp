#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads the runtime lifecycle sink source for structural assertions.
std::string ReadLifecycleSinkSource() {
    std::ifstream file(DOVAHLINK_LIFECYCLE_SINK_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

///  Reads the runtime lifecycle sink header for structural assertions.
std::string ReadLifecycleSinkHeader() {
    std::ifstream file(DOVAHLINK_LIFECYCLE_SINK_HEADER_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  The application aggregate owns executable lifecycle serialization tests.
//  This structural test protects the runtime adapter's decode/delegate boundary
//  without linking CommonLib into the Skyrim-independent application test target.
TEST_CASE("CommonLibGameLifecycleSink delegates lifecycle transitions",
          "[game_state][lifecycle]") {
    const std::string source = ReadLifecycleSinkSource();
    const std::string header = ReadLifecycleSinkHeader();

    CHECK(header.find(
              "application::IPlayContextLifecycle& playContextLifecycle_") !=
          std::string::npos);
    CHECK(header.find(
              "class CommonLibGameLifecycleSink final : public "
              "ICommonLibGameLifecycleSink") != std::string::npos);
    CHECK(source.find(
              "application::IPlayContextLifecycle& playContextLifecycle") !=
          std::string::npos);
    CHECK(source.find("playContextLifecycle_.HandleEvent(event);") !=
          std::string::npos);
    CHECK(source.find("tracker_.HandleEvent(event);") == std::string::npos);
    CHECK(source.find("ApplyLifecycleTransition") == std::string::npos);
    CHECK(header.find("lifecycleMutex_") == std::string::npos);
    CHECK(source.find("HandleEvent(event, description);") !=
          std::string::npos);
    CHECK(source.find("application::RunContainedLifecycleWork(") !=
          std::string::npos);
    CHECK(source.find(
              "HandleEvent(application::LifecycleEvent::kRevert, \"Revert\");") !=
          std::string::npos);
}

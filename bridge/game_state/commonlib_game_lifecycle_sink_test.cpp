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

//  CommonLib-dependent runtime callbacks are intentionally excluded from the
//  application test binary. This structural test protects the DovahLink-owned
//  synchronization boundary without testing Skyrim internals as if they were
//  application code.
TEST_CASE("CommonLibGameLifecycleSink serializes tracker and context updates",
          "[game_state][lifecycle]") {
    const std::string source = ReadLifecycleSinkSource();
    const std::string header = ReadLifecycleSinkHeader();

    CHECK(header.find("std::mutex lifecycleMutex_") != std::string::npos);

    const std::size_t handlePos = source.find(
        "void CommonLibGameLifecycleSink::HandleEvent");
    REQUIRE(handlePos != std::string::npos);

    const std::size_t lockPos = source.find(
        "std::lock_guard<std::mutex> lifecycleLock(lifecycleMutex_);");
    REQUIRE(lockPos != std::string::npos);

    const std::size_t trackerPos = source.find("tracker_.HandleEvent(event);");
    REQUIRE(trackerPos != std::string::npos);

    const std::size_t contextPos =
        source.find("ApplyLifecycleTransition(activePlayContext_, transition);");
    REQUIRE(contextPos != std::string::npos);

    CHECK(handlePos < lockPos);
    CHECK(handlePos < trackerPos);
    CHECK(handlePos < contextPos);
    CHECK(lockPos < trackerPos);
    CHECK(lockPos < contextPos);
    CHECK(trackerPos < contextPos);
    CHECK(source.find("HandleEvent(event, description);") !=
          std::string::npos);
    CHECK(source.find(
              "HandleEvent(application::LifecycleEvent::kRevert, \"Revert\");") !=
          std::string::npos);
}

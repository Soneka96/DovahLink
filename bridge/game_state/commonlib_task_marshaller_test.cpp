#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads the runtime task-marshaller source for structural assertions.
std::string ReadTaskMarshallerSource() {
    std::ifstream file(DOVAHLINK_TASK_MARSHALLER_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  The test target intentionally does not link CommonLibSSE-NG. This
//  structural test protects the marshaller's contract against SKSE's task
//  interface without pretending to test SKSE internals as DovahLink
//  behavior.
TEST_CASE("CommonLibTaskMarshaller forwards to SKSE::GetTaskInterface",
          "[game_state][commonlib_task_marshaller]") {
    const std::string source = ReadTaskMarshallerSource();

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "SKSE::GetTaskInterface()->AddTask(std::move(task));"));
}

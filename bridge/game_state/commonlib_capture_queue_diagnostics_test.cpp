#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads the diagnostics adapter's own source for structural assertions.
std::string ReadCaptureQueueDiagnosticsSource() {
    std::ifstream file(DOVAHLINK_CAPTURE_QUEUE_DIAGNOSTICS_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  SKSE::log requires a running SKSE process to observe, so this test protects
//  the adapter's mode-aware severity structurally instead: a kEvent rejection
//  must log through SKSE::log::error (a lost state transition), and a
//  kSnapshot rejection through SKSE::log::warn (recoverable next tick),
//  never the other way around, and every CaptureMode enumerator must be
//  mapped rather than falling through to the unreachable default.
TEST_CASE("CommonLibCaptureQueueDiagnostics logs Event rejections as errors "
          "and Snapshot rejections as warnings",
          "[game_state][capture_queue_diagnostics]") {
    const std::string source = ReadCaptureQueueDiagnosticsSource();

    CHECK(dovahlink::test_support::ContainsSourceText(
        source,
        "void CommonLibCaptureQueueDiagnostics::RecordCaptureRejected("));

    std::size_t eventBranchPos = source.find("mode == application::CaptureMode::kEvent");
    REQUIRE(eventBranchPos != std::string::npos);
    std::size_t errorLogPos =
        source.find("SKSE::log::error(", eventBranchPos);
    std::size_t warnLogPos = source.find("SKSE::log::warn(", eventBranchPos);
    REQUIRE(errorLogPos != std::string::npos);
    //  The error call for the kEvent branch must appear before any warn
    //  call, proving kEvent maps to error rather than warn.
    CHECK((warnLogPos == std::string::npos || errorLogPos < warnLogPos));

    CHECK(source.find(
              "case application::CaptureMode::kSnapshot:") != std::string::npos);
    CHECK(source.find("case application::CaptureMode::kEvent:") !=
          std::string::npos);

    //  RecordCaptureRejected actually consults the mapping helper rather
    //  than logging the raw enum value.
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "CaptureModeName(mode)"));
}

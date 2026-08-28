#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

#include "test_support/source_text_test_support.hpp"

namespace {

///  Reads the diagnostics adapter's own source for structural assertions.
std::string ReadPublicationDiagnosticsSource() {
    std::ifstream file(DOVAHLINK_PUBLICATION_DIAGNOSTICS_SOURCE_FILE);
    REQUIRE(file.is_open());
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} //  namespace

//  SKSE::log requires a running SKSE process to observe, so this test protects
//  the adapter's contract and its DisconnectReason mapping structurally
//  instead: every IPublicationDiagnostics method must forward through
//  SKSE::log, and every DisconnectReason enumerator must be mapped rather
//  than falling through to the unreachable default.
TEST_CASE("CommonLibPublicationDiagnostics logs every reported signal and "
          "maps every DisconnectReason",
          "[game_state][publication_diagnostics]") {
    const std::string source = ReadPublicationDiagnosticsSource();

    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "void CommonLibPublicationDiagnostics::RecordQueueDepth("));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "void CommonLibPublicationDiagnostics::RecordCoalesced("));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source,
        "void CommonLibPublicationDiagnostics::RecordDequeueLatency("));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "void CommonLibPublicationDiagnostics::RecordRecovery("));
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "void CommonLibPublicationDiagnostics::RecordDisconnect("));

    //  Five Record* methods, each forwarding to SKSE::log::info exactly once,
    //  tagged with the same "[publication]" prefix.
    std::size_t loggedCount = 0;
    std::size_t taggedCount = 0;
    std::size_t position = 0;
    while ((position = source.find("SKSE::log::info(", position)) !=
           std::string::npos) {
        ++loggedCount;
        std::size_t quotePosition = source.find('"', position);
        if (quotePosition != std::string::npos &&
            source.compare(quotePosition, 14, "\"[publication]") == 0) {
            ++taggedCount;
        }
        position += 1;
    }
    CHECK(loggedCount == 5);
    CHECK(taggedCount == 5);

    CHECK(source.find("case application::DisconnectReason::kReservedLaneFull:") !=
          std::string::npos);
    CHECK(source.find("case application::DisconnectReason::kEventOverflow:") !=
          std::string::npos);
    CHECK(source.find("case application::DisconnectReason::kSendFailed:") !=
          std::string::npos);

    //  RecordDisconnect actually consults the mapping helper rather than
    //  logging the raw enum value.
    CHECK(dovahlink::test_support::ContainsSourceText(
        source, "DisconnectReasonName(reason)"));
}

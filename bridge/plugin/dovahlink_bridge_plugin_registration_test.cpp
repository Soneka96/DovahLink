#include <catch2/catch_test_macros.hpp>

#include <fstream>
#include <sstream>
#include <string>

namespace {

/// Counts non-overlapping occurrences of a substring in source text.
/// @param haystack Text to search.
/// @param needle Substring to count.
std::size_t CountOccurrences(const std::string& haystack, const std::string& needle) {
    std::size_t count = 0;
    std::size_t pos = 0;
    while ((pos = haystack.find(needle, pos)) != std::string::npos) {
        ++count;
        pos += needle.size();
    }
    return count;
}

}  // namespace

// SKSE::MessagingInterface::RegisterListener may be called at most once per
// plugin; a second call silently breaks both registrations instead of
// erroring loudly (see ai/context/skse/testing.md and the comment above the
// registration call in dovahlink_bridge_plugin.cpp). That failure mode plays
// out only inside a running SKSE process, so it cannot be caught by exercising
// plugin behavior in a unit test. This test instead enforces the invariant
// structurally, by asserting there is exactly one call site in the plugin
// entry point's source text -- every new messaging-interface concern must be
// added inside that single listener's dispatch, never via another
// RegisterListener call.
TEST_CASE("SKSEPluginLoad registers the SKSE messaging listener exactly once", "[plugin][registration]") {
    std::ifstream file(DOVAHLINK_PLUGIN_SOURCE_FILE);
    REQUIRE(file.is_open());

    std::ostringstream buffer;
    buffer << file.rdbuf();
    std::string source = buffer.str();

    // Text search only, not a C++ parse: a mention of "RegisterListener(" inside
    // a comment or string literal would also count. Acceptable for this file,
    // which does not otherwise reference the name in prose; raise the check to
    // a real parse only if that stops being true.
    CHECK(CountOccurrences(source, "RegisterListener(") == 1);
}

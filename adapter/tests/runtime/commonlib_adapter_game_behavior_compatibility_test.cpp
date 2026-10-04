#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <string_view>

using dovahlink::adapter::test_support::NormalizeWhitespace;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  Checks whether a normalized expression appears in the CommonLib runtime source.
///  @param source The source text to search.
///  @param expression The expression to find, ignoring whitespace.
///  @return Whether the expression appears in the source.
bool Contains(const std::string& source, std::string_view expression) {
    return NormalizeWhitespace(source).find(NormalizeWhitespace(expression)) !=
           std::string::npos;
}

} //  namespace

TEST_CASE("successful compatibility setup is Debug-only while failures remain visible",
          "[runtime][compatibility][logging][structural]") {
    const std::string source = ReadSource(
        DOVAHLINK_ADAPTER_GAME_BEHAVIOR_COMPATIBILITY_SOURCE_FILE);

    CHECK(Contains(source,
                   "SKSE::log::debug(\"Always-active mode applied "
                   "(bAlwaysActive:General set to true).\")"));
    CHECK(Contains(source,
                   "SKSE::log::debug(\"Achievement compatibility patch "
                   "installed.\")"));
    CHECK(!Contains(source,
                    "SKSE::log::info(\"Always-active mode applied "
                    "(bAlwaysActive:General set to true).\")"));
    CHECK(!Contains(source,
                    "SKSE::log::info(\"Achievement compatibility patch "
                    "installed.\")"));
    CHECK(Contains(source,
                   "SKSE::log::warn(\"Could not resolve the INI setting "
                   "collection;"));
    CHECK(Contains(source, "always-active mode was not applied."));
    CHECK(Contains(source, "SKSE::log::warn(\"Could not resolve the "
                           "achievement-eligibility function"));
    CHECK(Contains(source,
                   "address; achievement compatibility was not"));
    CHECK(Contains(source, "applied."));
}

#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::test_support::ContainsSourceText;
using dovahlink::test_support::FindSourceText;

TEST_CASE("source text matching ignores formatter layout", "[test_support]") {
    const std::string source =
        "RE::BSFixedString Reset(RE::StaticFunctionTag *, RE::BSFixedString akCode)";

    CHECK(ContainsSourceText(source,
                             "RE::BSFixedString Reset(RE::StaticFunctionTag*, "
                             "RE::BSFixedString akCode)"));
    CHECK(FindSourceText(source, "Reset(RE::StaticFunctionTag*)") != std::string::npos);
}

TEST_CASE("source text matching joins adjacent string literals", "[test_support]") {
    const std::string source =
        R"(SKSE::log::error("Bridge requires " "Windows 10 or later."))";

    CHECK(ContainsSourceText(source, "requires Windows 10 or later."));
}

TEST_CASE("source text matching ignores tabs and punctuation layout", "[test_support]") {
    const std::string source = "void f(\n\tconst Value & value,\n\tint count\n);";

    CHECK(ContainsSourceText(source, "void f(const Value& value, int count);"));
}

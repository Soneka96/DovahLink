#pragma once

#include <cctype>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <string_view>

#include <catch2/catch_test_macros.hpp>

namespace dovahlink::adapter::test_support {

///  Reads a source file's own text for structural, text-scan-only test
///  assertions -- never for compiling or linking it.
inline std::string ReadSource(const std::filesystem::path& path) {
    std::ifstream file(path);
    REQUIRE(file.is_open());
    std::ostringstream contents;
    contents << file.rdbuf();
    return contents.str();
}

///  Removes every whitespace character from `text`. Apply to both a source
///  string and a `.find()` needle so a structural assertion pins the
///  architectural invariant a symbol/allocation/call shape expresses, not
///  clang-format's current indentation, line-wrapping, or pointer/reference
///  spacing -- all of which are legitimate to reformat later.
///  @param text The source or needle text to strip whitespace from.
///  @return `text` with every whitespace character removed.
inline std::string NormalizeWhitespace(std::string_view text) {
    std::string result;
    result.reserve(text.size());
    for (char character : text) {
        if (!std::isspace(static_cast<unsigned char>(character))) {
            result.push_back(character);
        }
    }
    return result;
}

} //  namespace dovahlink::adapter::test_support

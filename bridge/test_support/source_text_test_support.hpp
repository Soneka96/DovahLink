#pragma once

#include <cctype>
#include <cstddef>
#include <string>
#include <string_view>

namespace dovahlink::test_support {

///  Normalizes source text for structural assertions without changing token order.
inline std::string NormalizeSourceText(std::string_view source) {
    std::string normalized;
    normalized.reserve(source.size());
    const auto isPunctuation = [](char character) {
        return character == '*' || character == '&' || character == '(' ||
               character == ')' || character == ',' || character == ';';
    };
    bool pendingSpace = false;
    for (const unsigned char character : source) {
        if (character == '"') {
            continue;
        }
        if (std::isspace(character) != 0) {
            pendingSpace = true;
            continue;
        }
        if (pendingSpace && !normalized.empty() && !isPunctuation(normalized.back()) &&
            !isPunctuation(static_cast<char>(character))) {
            normalized.push_back(' ');
        }
        pendingSpace = false;
        normalized.push_back(static_cast<char>(character));
    }
    return normalized;
}

///  Returns whether source contains a formatting-insensitive source snippet.
inline bool ContainsSourceText(std::string_view source, std::string_view snippet) {
    return NormalizeSourceText(source).find(NormalizeSourceText(snippet)) !=
           std::string::npos;
}

///  Finds a formatting-insensitive source snippet after a normalized offset.
inline std::size_t FindSourceText(std::string_view source, std::string_view snippet,
                                  std::size_t start = 0) {
    return NormalizeSourceText(source).find(NormalizeSourceText(snippet), start);
}

} //  namespace dovahlink::test_support

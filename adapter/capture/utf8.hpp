#pragma once

#include <cstddef>
#include <string_view>

namespace dovahlink::adapter::capture::detail {

///  Checks that a byte string contains valid UTF-8 without embedded NUL.
///  @param text The byte string to check.
///  @return `true` when every sequence encodes a Unicode scalar value.
inline bool IsValidUtf8(std::string_view text) {
    std::size_t index = 0;
    while (index < text.size()) {
        const auto first = static_cast<unsigned char>(text[index]);
        if (first <= 0x7F) {
            if (first == 0) {
                return false;
            }
            ++index;
            continue;
        }

        std::size_t continuationCount = 0;
        unsigned char secondMinimum = 0x80;
        unsigned char secondMaximum = 0xBF;
        if (first >= 0xC2 && first <= 0xDF) {
            continuationCount = 1;
        } else if (first >= 0xE0 && first <= 0xEF) {
            continuationCount = 2;
            if (first == 0xE0) {
                secondMinimum = 0xA0;
            } else if (first == 0xED) {
                secondMaximum = 0x9F;
            }
        } else if (first >= 0xF0 && first <= 0xF4) {
            continuationCount = 3;
            if (first == 0xF0) {
                secondMinimum = 0x90;
            } else if (first == 0xF4) {
                secondMaximum = 0x8F;
            }
        } else {
            return false;
        }

        if (index + continuationCount >= text.size()) {
            return false;
        }
        const auto second = static_cast<unsigned char>(text[index + 1]);
        if (second < secondMinimum || second > secondMaximum) {
            return false;
        }
        for (std::size_t offset = 2; offset <= continuationCount; ++offset) {
            const auto continuation = static_cast<unsigned char>(text[index + offset]);
            if (continuation < 0x80 || continuation > 0xBF) {
                return false;
            }
        }
        index += continuationCount + 1;
    }
    return true;
}

} //  namespace dovahlink::adapter::capture::detail

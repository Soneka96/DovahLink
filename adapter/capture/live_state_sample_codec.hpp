#pragma once

#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>

namespace dovahlink::adapter::capture {

///  Encodes `value` as 4 little-endian bytes, matching the host's own
///  `BinaryPrimitives.ReadSingleLittleEndian` decode (see
///  `LiveCaptureSink.cs`'s `TryDecodeFiniteFloat`). Shared, CommonLib-free
///  logic so the CommonLib-backed capture router's wire encoding can be
///  proven by a real unit test rather than only a source-text structural
///  check, per `ai/context/adapter/architecture.md`'s "Technology boundary".
inline std::array<std::byte, 4> EncodeFloatLittleEndian(float value) {
    std::uint32_t bits = std::bit_cast<std::uint32_t>(value);
    return {
        static_cast<std::byte>(bits & 0xFFu),
        static_cast<std::byte>((bits >> 8) & 0xFFu),
        static_cast<std::byte>((bits >> 16) & 0xFFu),
        static_cast<std::byte>((bits >> 24) & 0xFFu),
    };
}

///  Encodes `value` as 2 little-endian bytes, matching the host's own
///  `BinaryPrimitives.ReadUInt16LittleEndian` decode (see
///  `LiveCaptureSink.cs`'s `ApplyLevel`).
inline std::array<std::byte, 2> EncodeUInt16LittleEndian(std::uint16_t value) {
    return {
        static_cast<std::byte>(value & 0xFFu),
        static_cast<std::byte>((value >> 8) & 0xFFu),
    };
}

} //  namespace dovahlink::adapter::capture

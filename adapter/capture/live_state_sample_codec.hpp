#pragma once

#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

#include "capture/captured_payload.hpp"

namespace dovahlink::adapter::capture {

///  Copies an arbitrary runtime `source` into a fresh `CapturedPayload`, or
///  fails closed with `std::nullopt` if it exceeds `kMaxCapturedPayloadBytes`
///  rather than truncating it.
inline std::optional<CapturedPayload>
TryMakeCapturedPayload(std::span<const std::byte> source) {
    if (source.size() > kMaxCapturedPayloadBytes) {
        return std::nullopt;
    }
    CapturedPayload payload;
    std::ranges::copy(source, payload.bytes.begin());
    payload.size = static_cast<std::uint8_t>(source.size());
    return payload;
}

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

#pragma once

#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

#include "constants.hpp"

namespace dovahlink::adapter::capture {

//  TODO(stage4-file-extraction): Move CapturedPayload and MakeCapturedPayload
//  to their own capture/captured_payload.hpp in the post-Stage-4 structural
//  cleanup PR. Temporarily colocated here to hold this PR's changed-file
//  count down; extraction only, no behavior change.
///  An owned captured value in a fixed, preallocated buffer sized for the
///  largest capture any current capture unit produces
///  (`kMaxCapturedPayloadBytes`), so building one -- on the Skyrim game
///  thread, at the capture boundary -- never allocates. Unused trailing
///  bytes beyond `size` are always zero, so structural equality over the
///  whole fixed buffer is well-defined regardless of a shorter payload's
///  actual length.
struct CapturedPayload {
    ///  The captured bytes, left-aligned from index 0; only the first `size`
    ///  are meaningful.
    std::array<std::byte, kMaxCapturedPayloadBytes> bytes{};
    ///  How many leading bytes of `bytes` are actually meaningful.
    std::uint8_t size = 0;

    ///  A read-only view of exactly the meaningful bytes.
    std::span<const std::byte> AsSpan() const {
        return std::span(bytes).first(size);
    }

    ///  Structural equality over every field.
    bool operator==(const CapturedPayload&) const = default;
};

///  Copies a compile-time fixed-size `source` into a fresh `CapturedPayload`.
///  `N` is checked against `kMaxCapturedPayloadBytes` at compile time, so
///  this overload can never be built for an oversized `source`.
template <std::size_t N>
CapturedPayload MakeCapturedPayload(const std::array<std::byte, N>& source) {
    static_assert(N <= kMaxCapturedPayloadBytes,
                  "source exceeds CapturedPayload's fixed capacity");
    CapturedPayload payload;
    std::ranges::copy(source, payload.bytes.begin());
    payload.size = static_cast<std::uint8_t>(N);
    return payload;
}

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

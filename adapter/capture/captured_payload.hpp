#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

#include "constants.hpp"

namespace dovahlink::adapter::capture {

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

} //  namespace dovahlink::adapter::capture

#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

#include "enums.hpp"

namespace dovahlink::adapter::capture {

///  An owned, bounded capture result produced at a Skyrim callback boundary and
///  handed off to worker-owned code. Holds no Skyrim/CommonLib pointer or
///  borrowed buffer; every field is a plain owned value that remains valid for
///  as long as the item itself does.
struct AdapterCaptureWorkItem {
    ///  The host-directed event key or sample token this value was captured for.
    std::uint32_t intentKey = 0;
    ///  The captured value, already copied out of Skyrim state at the callback
    ///  boundary. Empty when `availability` is `kUnavailable`.
    std::vector<std::byte> capturedValue;
    ///  The originating `IpcReadSampleMessage`'s correlation id, or zero for a
    ///  capture with no originating host request.
    std::uint64_t correlationId = 0;
    ///  Which host-owned key namespace `intentKey` belongs to.
    CaptureSourceKind source = CaptureSourceKind::kSample;
    ///  Whether `capturedValue` holds a real captured value.
    CaptureAvailability availability = CaptureAvailability::kAvailable;
    ///  The play context that was current at the moment this value was
    ///  captured, stamped at the same callback boundary rather than read
    ///  later by worker-owned code -- so a value captured just before a save
    ///  transition can never be misattributed to a context it was not
    ///  actually captured under. All-zero whenever no play context is
    ///  currently active: before the first one is ever established, or after
    ///  one has ended (loading has started, or the player returned to the
    ///  main menu) with no later one established yet.
    std::array<std::byte, 16> playContextId{};

    ///  Structural equality over every field.
    bool operator==(const AdapterCaptureWorkItem&) const = default;
};

} //  namespace dovahlink::adapter::capture

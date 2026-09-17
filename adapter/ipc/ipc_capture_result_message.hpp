#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

#include "enums.hpp"

namespace dovahlink::adapter::ipc {

///  Reports one captured value, or its unavailability, to the host.
///  `correlationId` matches the originating `IpcReadSampleMessage` for a
///  sampled capture, or is zero for a capture with no originating host
///  request (for example a future spontaneous native-event capture).
struct IpcCaptureResultMessage {
    ///  Matches the originating request, or zero. See the type documentation.
    std::uint64_t correlationId = 0;
    ///  Which host-owned key namespace `captureKey` belongs to.
    capture::CaptureSourceKind source = capture::CaptureSourceKind::kSample;
    ///  The host-owned sample token or event key this result was captured for.
    std::uint32_t captureKey = 0;
    ///  Whether `payload` holds a real captured value.
    capture::CaptureAvailability availability =
        capture::CaptureAvailability::kAvailable;
    ///  The captured value, already copied out of Skyrim state; empty when
    ///  `availability` is `kUnavailable`.
    std::vector<std::byte> payload;

    ///  Structural equality over every field.
    bool operator==(const IpcCaptureResultMessage&) const = default;
};

} //  namespace dovahlink::adapter::ipc

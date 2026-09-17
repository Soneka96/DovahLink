#pragma once

#include <cstddef>
#include <vector>

#include "enums.hpp"

namespace dovahlink::adapter::dispatch {

///  The result of one `IAdapterNativeCaptureRouter::CaptureSample` call.
struct SampleCaptureResult {
    ///  Which of the three outcomes this call produced.
    SampleCaptureStatus status = SampleCaptureStatus::kUnsupported;
    ///  The captured value when `status` is `kAvailable`; empty otherwise.
    std::vector<std::byte> payload;

    ///  Structural equality over every field.
    bool operator==(const SampleCaptureResult&) const = default;
};

} //  namespace dovahlink::adapter::dispatch

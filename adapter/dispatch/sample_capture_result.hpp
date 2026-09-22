#pragma once

#include "capture/captured_payload.hpp"
#include "enums.hpp"

namespace dovahlink::adapter::dispatch {

///  The result of one `IAdapterNativeCaptureRouter::CaptureSample` call.
struct SampleCaptureResult {
    ///  Which of the three outcomes this call produced.
    SampleCaptureStatus status = SampleCaptureStatus::kUnsupported;
    ///  The captured value when `status` is `kAvailable`; empty otherwise.
    ///  A fixed, preallocated buffer -- never a heap allocation -- since this
    ///  result is built synchronously on the Skyrim game thread.
    capture::CapturedPayload payload;

    ///  Structural equality over every field.
    bool operator==(const SampleCaptureResult&) const = default;
};

} //  namespace dovahlink::adapter::dispatch

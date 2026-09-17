#include "dispatch/adapter_native_capture_router.hpp"

namespace dovahlink::adapter::dispatch {

SampleCaptureResult
AdapterNativeCaptureRouter::CaptureSample(std::uint32_t /*sampleToken*/) {
    //  No production sample token is registered yet.
    return SampleCaptureResult{.status = SampleCaptureStatus::kUnsupported};
}

bool AdapterNativeCaptureRouter::RegisterEvent(std::uint32_t /*eventKey*/) {
    //  No production event key is registered yet.
    return false;
}

} //  namespace dovahlink::adapter::dispatch

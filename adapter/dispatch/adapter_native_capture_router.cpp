#include "dispatch/adapter_native_capture_router.hpp"

namespace dovahlink::adapter::dispatch {

std::optional<std::vector<std::byte>>
AdapterNativeCaptureRouter::CaptureSample(std::uint32_t /*sampleToken*/) {
    //  No production sample token is registered yet.
    return std::nullopt;
}

bool AdapterNativeCaptureRouter::RegisterEvent(std::uint32_t /*eventKey*/) {
    //  No production event key is registered yet.
    return false;
}

} //  namespace dovahlink::adapter::dispatch

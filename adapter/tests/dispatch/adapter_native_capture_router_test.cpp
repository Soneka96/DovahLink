#include "dispatch/adapter_native_capture_router.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <limits>

using dovahlink::adapter::dispatch::AdapterNativeCaptureRouter;

TEST_CASE("AdapterNativeCaptureRouter has no registered sample translation "
          "yet, for any sample token",
          "[dispatch][adapter_native_capture_router]") {
    AdapterNativeCaptureRouter router;

    for (std::uint32_t sampleToken :
         {std::uint32_t{0}, std::uint32_t{1}, std::uint32_t{42},
          std::numeric_limits<std::uint32_t>::max()}) {
        CHECK_FALSE(router.CaptureSample(sampleToken).has_value());
    }
}

TEST_CASE("AdapterNativeCaptureRouter has no approved event registration "
          "yet, for any event key",
          "[dispatch][adapter_native_capture_router]") {
    AdapterNativeCaptureRouter router;

    for (std::uint32_t eventKey :
         {std::uint32_t{0}, std::uint32_t{1}, std::uint32_t{42},
          std::numeric_limits<std::uint32_t>::max()}) {
        CHECK_FALSE(router.RegisterEvent(eventKey));
    }
}

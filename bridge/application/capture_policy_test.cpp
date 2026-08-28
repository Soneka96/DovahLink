#include "application/capture_policy.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::application::CapturePolicy;
using dovahlink::application::CapturePolicyKind;
using dovahlink::application::RateClass;

TEST_CASE("CapturePolicy::NativeEvent has no rate class",
          "[application][capture_policy]") {
    CapturePolicy policy = CapturePolicy::NativeEvent();

    CHECK(policy.Kind() == CapturePolicyKind::kNativeEvent);
    CHECK_FALSE(policy.SampledSchedule().has_value());
}

TEST_CASE("CapturePolicy::Sampled carries the given rate class",
          "[application][capture_policy]") {
    CapturePolicy fast = CapturePolicy::Sampled(RateClass::kFast);
    CapturePolicy medium = CapturePolicy::Sampled(RateClass::kMedium);
    CapturePolicy slow = CapturePolicy::Sampled(RateClass::kSlow);

    CHECK(fast.Kind() == CapturePolicyKind::kSampled);
    REQUIRE(fast.SampledSchedule().has_value());
    CHECK(*fast.SampledSchedule() == RateClass::kFast);

    CHECK(medium.Kind() == CapturePolicyKind::kSampled);
    REQUIRE(medium.SampledSchedule().has_value());
    CHECK(*medium.SampledSchedule() == RateClass::kMedium);

    CHECK(slow.Kind() == CapturePolicyKind::kSampled);
    REQUIRE(slow.SampledSchedule().has_value());
    CHECK(*slow.SampledSchedule() == RateClass::kSlow);
}

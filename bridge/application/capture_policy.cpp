#include "application/capture_policy.hpp"

namespace dovahlink::application {

CapturePolicy::CapturePolicy(CapturePolicyKind kind,
                             std::optional<RateClass> rateClass)
    : kind_(kind), rateClass_(rateClass) {}

CapturePolicy CapturePolicy::NativeEvent() {
    return CapturePolicy(CapturePolicyKind::kNativeEvent, std::nullopt);
}

CapturePolicy CapturePolicy::Sampled(RateClass rateClass) {
    return CapturePolicy(CapturePolicyKind::kSampled, rateClass);
}

CapturePolicyKind CapturePolicy::Kind() const { return kind_; }

std::optional<RateClass> CapturePolicy::SampledSchedule() const {
    return rateClass_;
}

} //  namespace dovahlink::application

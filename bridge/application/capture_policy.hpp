#pragma once

#include "shared/enums.hpp"

#include <optional>

namespace dovahlink::application {

///  How one captured value is supplied to the bridge. Every instance is
///  either `kNativeEvent` (no rate class) or `kSampled` with a `RateClass`;
///  the two fields cannot disagree because construction is only possible
///  through `NativeEvent()` or `Sampled()`.
class CapturePolicy final {
  public:
    ///  Creates a native-event capture policy.
    [[nodiscard]] static CapturePolicy NativeEvent();

    ///  Creates a sampled capture policy bounded by the given rate class.
    ///  @param rateClass Maximum capture frequency.
    [[nodiscard]] static CapturePolicy Sampled(RateClass rateClass);

    ///  Returns how the captured value is supplied.
    [[nodiscard]] CapturePolicyKind Kind() const;

    ///  Returns the sampled rate class, or no value for `kNativeEvent`.
    [[nodiscard]] std::optional<RateClass> SampledSchedule() const;

  private:
    ///  Constructs a policy from an already-validated kind and rate class.
    ///  @param kind How the captured value is supplied.
    ///  @param rateClass Rate class, present only when `kind` is `kSampled`.
    CapturePolicy(CapturePolicyKind kind, std::optional<RateClass> rateClass);

    ///  How the captured value is supplied.
    CapturePolicyKind kind_;

    ///  Sampled rate class, or no value for `kNativeEvent`.
    std::optional<RateClass> rateClass_;
};

} //  namespace dovahlink::application

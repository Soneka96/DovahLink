#include "application/capture_policy_registry.hpp"

#include <catch2/catch_test_macros.hpp>

using dovahlink::application::CapturePolicy;
using dovahlink::application::CapturePolicyKind;
using dovahlink::application::CapturePolicyRegistry;
using dovahlink::application::RateClass;

TEST_CASE("PolicyFor returns no value before the key is registered",
          "[application][capture_policy_registry]") {
    CapturePolicyRegistry registry;

    CHECK_FALSE(registry.PolicyFor("character_level").has_value());
}

TEST_CASE("PolicyFor returns a registered NativeEvent policy",
          "[application][capture_policy_registry]") {
    CapturePolicyRegistry registry;

    registry.Register("character_level", CapturePolicy::NativeEvent());

    auto policy = registry.PolicyFor("character_level");
    REQUIRE(policy.has_value());
    CHECK(policy->Kind() == CapturePolicyKind::kNativeEvent);
    CHECK_FALSE(policy->SampledSchedule().has_value());
}

TEST_CASE("PolicyFor returns a registered Sampled policy with its rate class",
          "[application][capture_policy_registry]") {
    CapturePolicyRegistry registry;

    registry.Register("character_xp",
                      CapturePolicy::Sampled(RateClass::kFast));

    auto policy = registry.PolicyFor("character_xp");
    REQUIRE(policy.has_value());
    CHECK(policy->Kind() == CapturePolicyKind::kSampled);
    REQUIRE(policy->SampledSchedule().has_value());
    CHECK(*policy->SampledSchedule() == RateClass::kFast);
}

TEST_CASE("Register replaces a previously registered policy for the "
          "same key",
          "[application][capture_policy_registry]") {
    CapturePolicyRegistry registry;
    registry.Register("character_xp",
                      CapturePolicy::Sampled(RateClass::kFast));

    registry.Register("character_xp",
                      CapturePolicy::Sampled(RateClass::kSlow));

    auto policy = registry.PolicyFor("character_xp");
    REQUIRE(policy.has_value());
    REQUIRE(policy->SampledSchedule().has_value());
    CHECK(*policy->SampledSchedule() == RateClass::kSlow);
}

TEST_CASE("PolicyFor distinguishes between different registered keys",
          "[application][capture_policy_registry]") {
    CapturePolicyRegistry registry;
    registry.Register("character_level", CapturePolicy::NativeEvent());
    registry.Register("character_xp",
                      CapturePolicy::Sampled(RateClass::kFast));

    CHECK(registry.PolicyFor("character_level")->Kind() ==
          CapturePolicyKind::kNativeEvent);
    CHECK(registry.PolicyFor("character_xp")->Kind() ==
          CapturePolicyKind::kSampled);
    CHECK_FALSE(registry.PolicyFor("character_health").has_value());
}

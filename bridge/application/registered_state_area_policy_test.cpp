#include "application/registered_state_area_policy.hpp"

#include "application/constants.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>

using dovahlink::application::kMaxRegisteredStateAreas;
using dovahlink::application::RegisteredStateAreaPolicy;

TEST_CASE("IsRegistered is false before any key is registered",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;

    CHECK_FALSE(policy.IsRegistered("character_level"));
}

TEST_CASE("TryRegister admits a new key and IsRegistered reports it",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;

    CHECK(policy.TryRegister("character_level"));

    CHECK(policy.IsRegistered("character_level"));
}

TEST_CASE("TryRegister for an already-registered key is an idempotent "
          "success",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;
    REQUIRE(policy.TryRegister("character_level"));

    CHECK(policy.TryRegister("character_level"));

    CHECK(policy.IsRegistered("character_level"));
}

TEST_CASE("IsRegistered distinguishes between different keys",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;
    REQUIRE(policy.TryRegister("character_level"));

    CHECK(policy.IsRegistered("character_level"));
    CHECK_FALSE(policy.IsRegistered("character_health"));
}

TEST_CASE("TryRegister rejects a new key once the bound is reached",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;
    for (std::size_t i = 0; i < kMaxRegisteredStateAreas; ++i) {
        REQUIRE(policy.TryRegister("area_" + std::to_string(i)));
    }

    CHECK_FALSE(policy.TryRegister("one_too_many"));

    CHECK_FALSE(policy.IsRegistered("one_too_many"));
}

TEST_CASE("TryRegister for an already-registered key still succeeds once "
          "the bound is reached",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;
    for (std::size_t i = 0; i < kMaxRegisteredStateAreas; ++i) {
        REQUIRE(policy.TryRegister("area_" + std::to_string(i)));
    }

    CHECK(policy.TryRegister("area_0"));
}

TEST_CASE("Rejection past the bound leaves every previously registered key "
          "registered and rejects further distinct keys",
          "[application][registered_state_area_policy]") {
    RegisteredStateAreaPolicy policy;
    for (std::size_t i = 0; i < kMaxRegisteredStateAreas; ++i) {
        REQUIRE(policy.TryRegister("area_" + std::to_string(i)));
    }
    REQUIRE_FALSE(policy.TryRegister("one_too_many"));

    for (std::size_t i = 0; i < kMaxRegisteredStateAreas; ++i) {
        CHECK(policy.IsRegistered("area_" + std::to_string(i)));
    }
    CHECK_FALSE(policy.TryRegister("also_rejected"));
    CHECK_FALSE(policy.IsRegistered("also_rejected"));
}

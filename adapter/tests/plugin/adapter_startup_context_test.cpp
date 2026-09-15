#include "plugin/adapter_startup_context.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <filesystem>

using dovahlink::adapter::plugin::AdapterStartupContext;

TEST_CASE("AdapterStartupContext preserves instance id, owner lifetime id, "
          "rendezvous path, and host executable path as one value") {
    AdapterStartupContext original{
        .instanceId = {.value = {std::byte{1}, std::byte{2}}},
        .ownerLifetimeId = {std::byte{3}, std::byte{4}},
        .rendezvousPath = "C:/rendezvous/host.json",
        .hostExecutablePath = "C:/host/DovahLink.Host.exe",
    };

    AdapterStartupContext copy = original;

    CHECK(copy == original);
    CHECK(copy.instanceId == original.instanceId);
    CHECK(copy.ownerLifetimeId == original.ownerLifetimeId);
    CHECK(copy.rendezvousPath == std::filesystem::path("C:/rendezvous/host.json"));
    CHECK(copy.hostExecutablePath ==
          std::filesystem::path("C:/host/DovahLink.Host.exe"));
}

TEST_CASE("AdapterStartupContext equality distinguishes every field") {
    AdapterStartupContext original{
        .instanceId = {.value = {std::byte{1}}},
        .ownerLifetimeId = {std::byte{2}},
        .rendezvousPath = "C:/rendezvous/host.json",
        .hostExecutablePath = "C:/host/DovahLink.Host.exe",
    };

    CHECK_FALSE((AdapterStartupContext{
                    .instanceId = {.value = {std::byte{9}}},
                    .ownerLifetimeId = {std::byte{2}},
                    .rendezvousPath = "C:/rendezvous/host.json",
                    .hostExecutablePath = "C:/host/DovahLink.Host.exe",
                }) == original);
    CHECK_FALSE((AdapterStartupContext{
                    .instanceId = {.value = {std::byte{1}}},
                    .ownerLifetimeId = {std::byte{9}},
                    .rendezvousPath = "C:/rendezvous/host.json",
                    .hostExecutablePath = "C:/host/DovahLink.Host.exe",
                }) == original);
    CHECK_FALSE((AdapterStartupContext{
                    .instanceId = {.value = {std::byte{1}}},
                    .ownerLifetimeId = {std::byte{2}},
                    .rendezvousPath = "C:/different/host.json",
                    .hostExecutablePath = "C:/host/DovahLink.Host.exe",
                }) == original);
    CHECK_FALSE((AdapterStartupContext{
                    .instanceId = {.value = {std::byte{1}}},
                    .ownerLifetimeId = {std::byte{2}},
                    .rendezvousPath = "C:/rendezvous/host.json",
                    .hostExecutablePath = "C:/different/DovahLink.Host.exe",
                }) == original);
}

#include "application/trust_device_admin_service.hpp"

#include <catch2/catch_test_macros.hpp>
#include <fakeit.hpp>

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <utility>
#include <vector>

using dovahlink::application::ActiveSessionDisconnector;
using dovahlink::application::ITrustMutationCoordinator;
using dovahlink::application::TrustDeviceAdminService;
using dovahlink::security::BlockOutcome;
using dovahlink::security::ForgetOutcome;
using dovahlink::security::ITrustDeviceStore;
using dovahlink::security::KnownDeviceRecord;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::UnblockOutcome;
using fakeit::Mock;
using fakeit::Verify;
using fakeit::VerifyNoOtherInvocations;
using fakeit::When;

namespace {

///  Builds a representative known-device record for service interaction tests.
KnownDeviceRecord MakeDevice(std::string clientId, std::string shortId,
                             std::optional<std::string> displayName,
                             KnownDeviceState state, int createdAtSeconds) {
    return KnownDeviceRecord{
        .clientId = std::move(clientId),
        .credential = state == KnownDeviceState::kTrusted
                          ? std::vector<std::uint8_t>{1, 2}
                          : std::vector<std::uint8_t>{},
        .shortId = std::move(shortId),
        .displayName = std::move(displayName),
        .state = state,
        .createdAt = std::chrono::system_clock::time_point(
            std::chrono::seconds(createdAtSeconds)),
    };
}

} //  namespace

TEST_CASE("TrustDeviceAdminService lists trusted devices through its port",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;

    auto device = MakeDevice("client-1", "11111", std::string("Phone"),
                             KnownDeviceState::kTrusted, 1);
    When(Method(deviceStore, ListTrusted))
        .Return(std::vector<KnownDeviceRecord>{device});

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.List("trust") == "1 trusted client:\n11111  Phone");
    Verify(Method(deviceStore, ListTrusted)).Once();
    VerifyNoOtherInvocations(deviceStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

TEST_CASE("TrustDeviceAdminService revokes and disconnects a trusted device",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;

    auto device = MakeDevice("client-1", "11111", std::string("Phone"),
                             KnownDeviceState::kTrusted, 1);
    std::vector<std::string> interactions;
    When(Method(deviceStore, ListTrusted))
        .Return(std::vector<KnownDeviceRecord>{device});
    When(Method(deviceStore, Revoke)).Do([&](const std::string& clientId) {
        CHECK(clientId == "client-1");
        interactions.push_back("revoke");
        return true;
    });
    When(Method(sessionDisconnector, DisconnectIfClientActive))
        .Do([&](std::string_view clientId, std::string_view reason) {
            CHECK(clientId == "client-1");
            CHECK(reason == "revoked");
            interactions.push_back("disconnect");
        });

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.RevokeByShortId("11111") ==
          "Revoked client 11111 (Phone).");
    CHECK(interactions == std::vector<std::string>{"revoke", "disconnect"});
    Verify(Method(deviceStore, ListTrusted)).Once();
    Verify(Method(deviceStore, Revoke)).Once();
    Verify(Method(sessionDisconnector, DisconnectIfClientActive)).Once();
    VerifyNoOtherInvocations(deviceStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

TEST_CASE("TrustDeviceAdminService blocks, cancels pairing, and disconnects",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;

    auto device = MakeDevice("client-1", "11111", std::nullopt,
                             KnownDeviceState::kRevoked, 1);
    const auto now = std::chrono::steady_clock::time_point(
        std::chrono::seconds(42));
    std::vector<std::string> interactions;
    When(Method(deviceStore, FindByShortId)).Do([&](std::string_view shortId) {
        CHECK(shortId == "11111");
        return std::optional<KnownDeviceRecord>{device};
    });
    When(Method(mutationCoordinator, Block)).Do([&](const std::string& clientId, std::chrono::steady_clock::time_point cancelledAt) {
        CHECK(clientId == "client-1");
        CHECK(cancelledAt == now);
        interactions.push_back("block");
        return BlockOutcome::kBlocked;
    });
    When(Method(sessionDisconnector, DisconnectIfClientActive))
        .Do([&](std::string_view clientId, std::string_view reason) {
            CHECK(clientId == "client-1");
            CHECK(reason == "blocked");
            interactions.push_back("disconnect");
        });

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.BlockByShortId("11111", now) ==
          "Blocked device 11111 ((no display name)).");
    CHECK(interactions ==
          std::vector<std::string>{"block", "disconnect"});
    Verify(Method(deviceStore, FindByShortId)).Once();
    Verify(Method(mutationCoordinator, Block)).Once();
    Verify(Method(sessionDisconnector, DisconnectIfClientActive)).Once();
    VerifyNoOtherInvocations(deviceStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

TEST_CASE("TrustDeviceAdminService leaves collaborators untouched on block failure",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;

    auto device = MakeDevice("client-1", "11111", std::nullopt,
                             KnownDeviceState::kTrusted, 1);
    When(Method(deviceStore, FindByShortId))
        .Return(std::optional<KnownDeviceRecord>{device});
    When(Method(mutationCoordinator, Block)).Return(BlockOutcome::kSaveFailed);

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.BlockByShortId("11111", {}) ==
          "Failed to block device 11111: trust-store save failed.");
    Verify(Method(deviceStore, FindByShortId)).Once();
    Verify(Method(mutationCoordinator, Block)).Once();
    VerifyNoOtherInvocations(deviceStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

TEST_CASE("TrustDeviceAdminService does not disconnect when revoke persistence fails",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    auto device = MakeDevice("client-1", "11111", std::string("Phone"),
                             KnownDeviceState::kTrusted, 1);
    When(Method(deviceStore, ListTrusted))
        .Return(std::vector<KnownDeviceRecord>{device});
    When(Method(deviceStore, Revoke)).Return(false);

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.RevokeByShortId("11111") ==
          "Failed to revoke client 11111: trust-store save failed.");
    Verify(Method(deviceStore, ListTrusted)).Once();
    Verify(Method(deviceStore, Revoke)).Once();
    VerifyNoOtherInvocations(deviceStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

TEST_CASE("TrustDeviceAdminService maps non-mutating Block outcomes without disconnecting",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    auto device = MakeDevice("client-1", "11111", std::nullopt,
                             KnownDeviceState::kBlocked, 1);
    When(Method(deviceStore, FindByShortId))
        .AlwaysReturn(std::optional<KnownDeviceRecord>{device});
    When(Method(mutationCoordinator, Block))
        .Return(BlockOutcome::kAlreadyBlocked);

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.BlockByShortId("11111", {}) ==
          "Device 11111 is already blocked.");
    Verify(Method(deviceStore, FindByShortId)).Once();
    Verify(Method(mutationCoordinator, Block)).Once();
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

TEST_CASE("TrustDeviceAdminService delegates unblock and forget mutations",
          "[application][trust_device_admin_service]") {
    Mock<ITrustDeviceStore> deviceStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;

    auto device = MakeDevice("client-1", "11111", std::nullopt,
                             KnownDeviceState::kBlocked, 1);
    When(Method(deviceStore, FindByShortId))
        .AlwaysReturn(std::optional<KnownDeviceRecord>{device});
    When(Method(deviceStore, Unblock)).Return(UnblockOutcome::kUnblocked);
    When(Method(deviceStore, Forget)).Return(ForgetOutcome::kForgotten);

    TrustDeviceAdminService service(deviceStore.get(), sessionDisconnector.get(),
                                    mutationCoordinator.get());

    CHECK(service.UnblockByShortId("11111") ==
          "Unblocked device 11111 ((no display name)).");
    CHECK(service.ForgetByShortId("11111") ==
          "Forgot device 11111 ((no display name)).");
    Verify(Method(deviceStore, FindByShortId)).Exactly(2);
    Verify(Method(deviceStore, Unblock)).Once();
    Verify(Method(deviceStore, Forget)).Once();
    VerifyNoOtherInvocations(deviceStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
}

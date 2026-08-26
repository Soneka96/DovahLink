#include "application/trust_device_admin_service.hpp"

#include "application/application_test_support.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <optional>
#include <string>
#include <vector>

using dovahlink::application::ActiveSessionDisconnector;
using dovahlink::application::ConnectionId;
using dovahlink::application::ITrustMutationCoordinator;
using dovahlink::application::PairingCommitResult;
using dovahlink::application::TrustDeviceAdminService;
using dovahlink::application::test_support::BuildKnownDeviceRecord;
using dovahlink::application::test_support::MockActiveSessionDisconnector;
using dovahlink::application::test_support::MockTrustDeviceStore;
using dovahlink::application::test_support::MockTrustMutationCoordinator;
using dovahlink::security::BlockOutcome;
using dovahlink::security::CancelOutcome;
using dovahlink::security::ConfirmCodeResult;
using dovahlink::security::ForgetOutcome;
using dovahlink::security::ITrustDeviceStore;
using dovahlink::security::KnownDeviceRecord;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::PairingCommitOutcome;
using dovahlink::security::UnblockOutcome;
using testing::StrictMock;

TEST_CASE("TrustDeviceAdminService lists trusted devices through its port",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111",
                                         std::string("Phone"),
                                         KnownDeviceState::kTrusted, 1);
    EXPECT_CALL(deviceStore, ListTrusted())
        .WillOnce(testing::Return(std::vector<KnownDeviceRecord>{device}));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.List("trust") == "1 trusted client:\n11111  Phone");
}

TEST_CASE("TrustDeviceAdminService revokes and disconnects a trusted device",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111",
                                         std::string("Phone"),
                                         KnownDeviceState::kTrusted, 1);
    const auto now = std::chrono::steady_clock::time_point(
        std::chrono::seconds(42));
    testing::InSequence sequence;
    EXPECT_CALL(deviceStore, ListTrusted())
        .WillOnce(testing::Return(std::vector<KnownDeviceRecord>{device}));
    EXPECT_CALL(mutationCoordinator, Revoke("client-1", now))
        .WillOnce(testing::Return(true));
    EXPECT_CALL(sessionDisconnector,
                DisconnectIfClientActive("client-1", "revoked"));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.RevokeByShortId("11111", now) ==
          "Revoked client 11111 (Phone).");
}

TEST_CASE("TrustDeviceAdminService blocks, cancels pairing, and disconnects",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111", std::nullopt,
                                         KnownDeviceState::kRevoked, 1);
    const auto now = std::chrono::steady_clock::time_point(
        std::chrono::seconds(42));
    testing::InSequence sequence;
    EXPECT_CALL(deviceStore, FindByShortId("11111"))
        .WillOnce(testing::Return(std::optional<KnownDeviceRecord>{device}));
    EXPECT_CALL(mutationCoordinator, Block("client-1", now))
        .WillOnce(testing::Return(BlockOutcome::kBlocked));
    EXPECT_CALL(sessionDisconnector,
                DisconnectIfClientActive("client-1", "blocked"));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.BlockByShortId("11111", now) ==
          "Blocked device 11111 ((no display name)).");
}

TEST_CASE("TrustDeviceAdminService leaves collaborators untouched on block failure",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111", std::nullopt,
                                         KnownDeviceState::kTrusted, 1);
    EXPECT_CALL(deviceStore, FindByShortId("11111"))
        .WillOnce(testing::Return(std::optional<KnownDeviceRecord>{device}));
    EXPECT_CALL(mutationCoordinator, Block)
        .WillOnce(testing::Return(BlockOutcome::kSaveFailed));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.BlockByShortId("11111", {}) ==
          "Failed to block device 11111: trust-store save failed.");
}

TEST_CASE("TrustDeviceAdminService does not disconnect when revoke persistence fails",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111",
                                         std::string("Phone"),
                                         KnownDeviceState::kTrusted, 1);
    const auto now = std::chrono::steady_clock::time_point(
        std::chrono::seconds(42));
    EXPECT_CALL(deviceStore, ListTrusted())
        .WillOnce(testing::Return(std::vector<KnownDeviceRecord>{device}));
    EXPECT_CALL(mutationCoordinator, Revoke("client-1", now))
        .WillOnce(testing::Return(false));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.RevokeByShortId("11111", now) ==
          "Failed to revoke client 11111: trust-store save failed.");
}

TEST_CASE("TrustDeviceAdminService maps non-mutating Block outcomes without disconnecting",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111", std::nullopt,
                                         KnownDeviceState::kBlocked, 1);
    EXPECT_CALL(deviceStore, FindByShortId("11111"))
        .WillOnce(testing::Return(std::optional<KnownDeviceRecord>{device}));
    EXPECT_CALL(mutationCoordinator, Block)
        .WillOnce(testing::Return(BlockOutcome::kAlreadyBlocked));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.BlockByShortId("11111", {}) ==
          "Device 11111 is already blocked.");
}

TEST_CASE("TrustDeviceAdminService delegates unblock and forget mutations",
          "[application][trust_device_admin_service]") {
    StrictMock<MockTrustDeviceStore> deviceStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    auto device = BuildKnownDeviceRecord("client-1", "11111", std::nullopt,
                                         KnownDeviceState::kBlocked, 1);
    EXPECT_CALL(deviceStore, FindByShortId("11111"))
        .Times(2)
        .WillRepeatedly(testing::Return(
            std::optional<KnownDeviceRecord>{device}));
    EXPECT_CALL(deviceStore, Unblock("client-1"))
        .WillOnce(testing::Return(UnblockOutcome::kUnblocked));
    EXPECT_CALL(deviceStore, Forget("client-1"))
        .WillOnce(testing::Return(ForgetOutcome::kForgotten));

    TrustDeviceAdminService service(deviceStore, sessionDisconnector,
                                    mutationCoordinator);

    CHECK(service.UnblockByShortId("11111") ==
          "Unblocked device 11111 ((no display name)).");
    CHECK(service.ForgetByShortId("11111") ==
          "Forgot device 11111 ((no display name)).");
}

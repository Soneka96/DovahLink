#include "application/trust_reset_service.hpp"

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
using dovahlink::application::TrustResetService;
using dovahlink::application::test_support::BuildKnownDeviceRecord;
using dovahlink::application::test_support::MockActiveSessionDisconnector;
using dovahlink::application::test_support::MockFactoryResetChallenge;
using dovahlink::application::test_support::MockTrustMutationCoordinator;
using dovahlink::application::test_support::MockTrustResetStore;
using dovahlink::security::BlockOutcome;
using dovahlink::security::CancelOutcome;
using dovahlink::security::ConfirmCodeResult;
using dovahlink::security::FactoryResetConfirmOutcome;
using dovahlink::security::IFactoryResetChallenge;
using dovahlink::security::ITrustResetStore;
using dovahlink::security::KnownDeviceRecord;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::PairingCommitOutcome;
using testing::StrictMock;

TEST_CASE("TrustResetService starts a Factory Reset through its challenge port",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryStart())
        .WillOnce(testing::Return(std::optional<std::string>{"654321"}));
    EXPECT_CALL(factoryResetChallenge, CodeTimeToLive())
        .WillOnce(testing::Return(std::chrono::seconds(60)));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 60 "
          "seconds to permanently erase all trust.");
}

TEST_CASE("TrustResetService formats a custom Factory Reset TTL",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryStart())
        .WillOnce(testing::Return(std::optional<std::string>{"654321"}));
    EXPECT_CALL(factoryResetChallenge, CodeTimeToLive())
        .WillOnce(testing::Return(std::chrono::milliseconds(1500)));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 2 seconds "
          "to permanently erase all trust.");
}

TEST_CASE("TrustResetService clamps a negative Factory Reset TTL to zero",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryStart())
        .WillOnce(testing::Return(std::optional<std::string>{"654321"}));
    EXPECT_CALL(factoryResetChallenge, CodeTimeToLive())
        .WillOnce(testing::Return(std::chrono::milliseconds(-1)));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 0 seconds "
          "to permanently erase all trust.");
}

TEST_CASE("TrustResetService reads the TTL after each Factory Reset start",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryStart())
        .WillOnce(testing::Return(std::optional<std::string>{"111111"}))
        .WillOnce(testing::Return(std::optional<std::string>{"222222"}));
    EXPECT_CALL(factoryResetChallenge, CodeTimeToLive())
        .WillOnce(testing::Return(std::chrono::seconds(60)))
        .WillOnce(testing::Return(std::chrono::seconds(90)));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 111111 within 60 seconds "
          "to permanently erase all trust.");
    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 222222 within 90 seconds "
          "to permanently erase all trust.");
}

TEST_CASE("TrustResetService reports Factory Reset code-generation failure",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryStart())
        .WillOnce(testing::Return(std::optional<std::string>{}));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.StartFactoryReset() ==
          "Failed to start Factory Reset: could not generate a confirmation code.");
}

TEST_CASE("TrustResetService does not mutate trust after an invalid code",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryConfirm("000000"))
        .WillOnce(testing::Return(FactoryResetConfirmOutcome::kInvalid));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.ConfirmFactoryReset("000000") ==
          "Wrong Factory Reset confirmation code; the challenge was cancelled. "
          "Start over with 'reset'.");
}

TEST_CASE("TrustResetService reports an expired Factory Reset challenge",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(factoryResetChallenge, TryConfirm("000000"))
        .WillOnce(testing::Return(FactoryResetConfirmOutcome::kExpired));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.ConfirmFactoryReset("000000") ==
          "No Factory Reset confirmation is pending; start one with 'reset' first.");
}

TEST_CASE("TrustResetService confirmed Factory Reset performs cleanup",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    testing::InSequence sequence;
    EXPECT_CALL(factoryResetChallenge, TryConfirm("654321"))
        .WillOnce(testing::Return(FactoryResetConfirmOutcome::kConfirmed));
    EXPECT_CALL(resetStore, ListTrusted())
        .WillOnce(testing::Return(std::vector<KnownDeviceRecord>{
            BuildKnownDeviceRecord("client-1", "11111", std::nullopt,
                                   KnownDeviceState::kTrusted, 1)}));
    EXPECT_CALL(mutationCoordinator, FactoryReset()).WillOnce(testing::Return(true));
    EXPECT_CALL(sessionDisconnector, DisconnectActive("factory_reset"));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.ConfirmFactoryReset("654321") ==
          "Factory Reset complete (1 trusted device erased).");
}

TEST_CASE("TrustResetService Reset Trust cancels and disconnects trusted clients",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    testing::InSequence sequence;
    EXPECT_CALL(mutationCoordinator, ResetTrust())
        .WillOnce(testing::Return(std::optional<std::vector<std::string>>{
            std::vector<std::string>{"client-1", "client-2"}}));
    EXPECT_CALL(sessionDisconnector,
                DisconnectIfClientActive("client-1", "trust_reset"));
    EXPECT_CALL(sessionDisconnector,
                DisconnectIfClientActive("client-2", "trust_reset"));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.ResetTrust() == "Reset Trust complete (2 devices revoked).");
}

TEST_CASE("TrustResetService Reset Trust failure skips cleanup",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(mutationCoordinator, ResetTrust())
        .WillOnce(testing::Return(std::optional<std::vector<std::string>>{}));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.ResetTrust() ==
          "Failed to reset trust: trust-store save failed.");
}

TEST_CASE("TrustResetService Reset Trust reports zero revoked devices",
          "[application][trust_reset_service]") {
    StrictMock<MockTrustResetStore> resetStore;
    StrictMock<MockActiveSessionDisconnector> sessionDisconnector;
    StrictMock<MockTrustMutationCoordinator> mutationCoordinator;
    StrictMock<MockFactoryResetChallenge> factoryResetChallenge;
    EXPECT_CALL(mutationCoordinator, ResetTrust())
        .WillOnce(testing::Return(std::optional<std::vector<std::string>>{
            std::vector<std::string>{}}));

    TrustResetService service(resetStore, sessionDisconnector,
                              mutationCoordinator, factoryResetChallenge);

    CHECK(service.ResetTrust() == "Reset Trust complete (0 devices revoked).");
}

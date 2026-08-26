#include "application/trust_reset_service.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <utility>
#include <vector>

using dovahlink::application::ActiveSessionDisconnector;
using dovahlink::application::ConnectionId;
using dovahlink::application::ITrustMutationCoordinator;
using dovahlink::application::PairingCommitResult;
using dovahlink::application::TrustResetService;
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

namespace {

///  Builds a representative trusted record for reset-count assertions.
KnownDeviceRecord MakeTrustedDevice(std::string clientId, std::string shortId,
                                    int createdAtSeconds) {
    return KnownDeviceRecord{
        .clientId = std::move(clientId),
        .credential = std::vector<std::uint8_t>{1, 2},
        .shortId = std::move(shortId),
        .displayName = std::nullopt,
        .state = KnownDeviceState::kTrusted,
        .createdAt = std::chrono::system_clock::time_point(
            std::chrono::seconds(createdAtSeconds)),
    };
}

///  GoogleMock bulk trust-store port double.
class MockTrustResetStore : public ITrustResetStore {
  public:
    MOCK_METHOD(std::vector<KnownDeviceRecord>, ListTrusted, (), (override));
    MOCK_METHOD(bool, Reset, (), (override));
    MOCK_METHOD(bool, ResetTrust, (), (override));
};

///  GoogleMock active-session disconnection port double.
class MockActiveSessionDisconnector : public ActiveSessionDisconnector {
  public:
    MOCK_METHOD(void, DisconnectIfClientActive,
                (std::string_view, std::string_view), (override));
    MOCK_METHOD(void, DisconnectActive, (std::string_view), (override));
};

///  GoogleMock trust-mutation coordination port double.
class MockTrustMutationCoordinator : public ITrustMutationCoordinator {
  public:
    MOCK_METHOD(ConfirmCodeResult, ConfirmPairing,
                (const std::string&, std::chrono::steady_clock::time_point,
                 std::string, std::vector<std::uint8_t>,
                 std::optional<std::string>),
                (override));
    MOCK_METHOD(PairingCommitResult, CommitPairing,
                (const std::string&, const std::vector<std::uint8_t>&,
                 std::chrono::steady_clock::time_point, ConnectionId,
                 const std::string&),
                (override));
    MOCK_METHOD(std::optional<KnownDeviceRecord>, PromoteAlreadyTrusted,
                (const std::string&, const std::vector<std::uint8_t>&,
                 ConnectionId, const std::string&),
                (override));
    MOCK_METHOD(CancelOutcome, TryCancel,
                (const std::string&, std::chrono::steady_clock::time_point),
                (override));
    MOCK_METHOD(void, CancelAll, (), (override));
    MOCK_METHOD(BlockOutcome, Block,
                (const std::string&, std::chrono::steady_clock::time_point),
                (override));
    MOCK_METHOD(bool, Revoke,
                (const std::string&, std::chrono::steady_clock::time_point),
                (override));
    MOCK_METHOD(std::optional<std::vector<std::string>>, ResetTrust, (),
                (override));
    MOCK_METHOD(bool, FactoryReset, (), (override));
};

///  GoogleMock Factory Reset challenge port double.
class MockFactoryResetChallenge : public IFactoryResetChallenge {
  public:
    MOCK_METHOD(std::optional<std::string>, TryStart, (), (override));
    MOCK_METHOD(std::chrono::steady_clock::duration, CodeTimeToLive, (),
                (const, override));
    MOCK_METHOD(FactoryResetConfirmOutcome, TryConfirm,
                (const std::string&), (override));
};

} //  namespace

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
            MakeTrustedDevice("client-1", "11111", 1)}));
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

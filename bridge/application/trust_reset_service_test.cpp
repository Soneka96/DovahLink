#include "application/trust_reset_service.hpp"

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
using dovahlink::application::TrustResetService;
using dovahlink::security::FactoryResetConfirmOutcome;
using dovahlink::security::IFactoryResetChallenge;
using dovahlink::security::ITrustResetStore;
using dovahlink::security::KnownDeviceRecord;
using dovahlink::security::KnownDeviceState;
using fakeit::Mock;
using fakeit::Verify;
using fakeit::VerifyNoOtherInvocations;
using fakeit::When;

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

} //  namespace

TEST_CASE("TrustResetService starts a Factory Reset through its challenge port",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;

    When(Method(factoryResetChallenge, TryStart))
        .Return(std::optional<std::string>{"654321"});
    When(Method(factoryResetChallenge, CodeTimeToLive))
        .Return(std::chrono::seconds(60));

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 60 "
          "seconds to permanently erase all trust.");
    Verify(Method(factoryResetChallenge, TryStart)).Once();
    Verify(Method(factoryResetChallenge, CodeTimeToLive)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService formats a custom Factory Reset TTL",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;
    When(Method(factoryResetChallenge, TryStart))
        .Return(std::optional<std::string>{"654321"});
    When(Method(factoryResetChallenge, CodeTimeToLive))
        .Return(std::chrono::milliseconds(1500));

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 2 seconds "
          "to permanently erase all trust.");
    Verify(Method(factoryResetChallenge, TryStart)).Once();
    Verify(Method(factoryResetChallenge, CodeTimeToLive)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService clamps a negative Factory Reset TTL to zero",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;
    When(Method(factoryResetChallenge, TryStart))
        .Return(std::optional<std::string>{"654321"});
    When(Method(factoryResetChallenge, CodeTimeToLive))
        .Return(std::chrono::milliseconds(-1));

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 0 seconds "
          "to permanently erase all trust.");
    Verify(Method(factoryResetChallenge, TryStart)).Once();
    Verify(Method(factoryResetChallenge, CodeTimeToLive)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService reads the TTL after each Factory Reset start",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;
    std::vector<std::string> interactions;
    int ttlCall = 0;
    When(Method(factoryResetChallenge, TryStart)).AlwaysDo([&]() {
        interactions.push_back("start");
        return std::optional<std::string>{ttlCall == 0 ? "111111" : "222222"};
    });
    When(Method(factoryResetChallenge, CodeTimeToLive)).AlwaysDo([&]() {
        interactions.push_back("ttl");
        return ++ttlCall == 1 ? std::chrono::seconds(60)
                              : std::chrono::seconds(90);
    });

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 111111 within 60 seconds "
          "to permanently erase all trust.");
    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 222222 within 90 seconds "
          "to permanently erase all trust.");
    CHECK(interactions ==
          std::vector<std::string>{"start", "ttl", "start", "ttl"});
    Verify(Method(factoryResetChallenge, TryStart)).Exactly(2);
    Verify(Method(factoryResetChallenge, CodeTimeToLive)).Exactly(2);
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService reports Factory Reset code-generation failure",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;
    When(Method(factoryResetChallenge, TryStart))
        .Return(std::optional<std::string>{});

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.StartFactoryReset() ==
          "Failed to start Factory Reset: could not generate a confirmation code.");
    Verify(Method(factoryResetChallenge, TryStart)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService does not mutate trust after an invalid code",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;

    When(Method(factoryResetChallenge, TryConfirm))
        .Return(FactoryResetConfirmOutcome::kInvalid);

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.ConfirmFactoryReset("000000") ==
          "Wrong Factory Reset confirmation code; the challenge was cancelled. "
          "Start over with 'reset'.");
    Verify(Method(factoryResetChallenge, TryConfirm)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService reports an expired Factory Reset challenge",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;
    When(Method(factoryResetChallenge, TryConfirm))
        .Return(FactoryResetConfirmOutcome::kExpired);

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.ConfirmFactoryReset("000000") ==
          "No Factory Reset confirmation is pending; start one with 'reset' first.");
    Verify(Method(factoryResetChallenge, TryConfirm)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService confirmed Factory Reset performs cleanup",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;

    When(Method(factoryResetChallenge, TryConfirm))
        .Do([&](const std::string& presentedCode) {
            CHECK(presentedCode == "654321");
            return FactoryResetConfirmOutcome::kConfirmed;
        });
    std::vector<std::string> interactions;
    When(Method(resetStore, ListTrusted)).Do([&]() {
        interactions.push_back("list");
        return std::vector<KnownDeviceRecord>{
            MakeTrustedDevice("client-1", "11111", 1)};
    });
    When(Method(mutationCoordinator, FactoryReset)).Do([&]() {
        interactions.push_back("reset");
        return true;
    });
    When(Method(sessionDisconnector, DisconnectActive)).Do([&](std::string_view reason) {
        CHECK(reason == "factory_reset");
        interactions.push_back("disconnect");
    });

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.ConfirmFactoryReset("654321") ==
          "Factory Reset complete (1 trusted device erased).");
    CHECK(interactions ==
          std::vector<std::string>{"list", "reset", "disconnect"});
    Verify(Method(factoryResetChallenge, TryConfirm)).Once();
    Verify(Method(resetStore, ListTrusted)).Once();
    Verify(Method(mutationCoordinator, FactoryReset)).Once();
    Verify(Method(sessionDisconnector, DisconnectActive)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService Reset Trust cancels and disconnects trusted clients",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;

    std::vector<std::string> interactions;
    When(Method(mutationCoordinator, ResetTrust)).Do([&]() {
        interactions.push_back("reset");
        return std::optional<std::vector<std::string>>{
            std::vector<std::string>{"client-1", "client-2"}};
    });
    When(Method(sessionDisconnector, DisconnectIfClientActive))
        .AlwaysDo([&](std::string_view clientId, std::string_view reason) {
            CHECK(reason == "trust_reset");
            interactions.push_back(std::string("disconnect:") +
                                   std::string(clientId));
        });

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.ResetTrust() ==
          "Reset Trust complete (2 devices revoked).");
    CHECK(interactions == std::vector<std::string>{
                              "reset", "disconnect:client-1",
                              "disconnect:client-2"});
    Verify(Method(mutationCoordinator, ResetTrust)).Once();
    Verify(Method(sessionDisconnector, DisconnectIfClientActive)).Exactly(2);
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService Reset Trust failure skips cleanup",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;

    When(Method(mutationCoordinator, ResetTrust))
        .Return(std::optional<std::vector<std::string>>{});

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.ResetTrust() ==
          "Failed to reset trust: trust-store save failed.");
    Verify(Method(mutationCoordinator, ResetTrust)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

TEST_CASE("TrustResetService Reset Trust reports zero revoked devices without disconnecting",
          "[application][trust_reset_service]") {
    Mock<ITrustResetStore> resetStore;
    Mock<ActiveSessionDisconnector> sessionDisconnector;
    Mock<ITrustMutationCoordinator> mutationCoordinator;
    Mock<IFactoryResetChallenge> factoryResetChallenge;

    When(Method(mutationCoordinator, ResetTrust))
        .Return(std::optional<std::vector<std::string>>{
            std::vector<std::string>{}});

    TrustResetService service(resetStore.get(), sessionDisconnector.get(),
                              mutationCoordinator.get(),
                              factoryResetChallenge.get());

    CHECK(service.ResetTrust() == "Reset Trust complete (0 devices revoked).");
    Verify(Method(mutationCoordinator, ResetTrust)).Once();
    VerifyNoOtherInvocations(resetStore);
    VerifyNoOtherInvocations(sessionDisconnector);
    VerifyNoOtherInvocations(mutationCoordinator);
    VerifyNoOtherInvocations(factoryResetChallenge);
}

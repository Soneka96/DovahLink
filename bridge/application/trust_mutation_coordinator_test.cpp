#include "application/trust_mutation_coordinator.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

using dovahlink::application::PairingCommitResult;
using dovahlink::application::TrustMutationCoordinator;
using dovahlink::security::BlockOutcome;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::PairingCommitOutcome;
using dovahlink::security::PairingSession;
using dovahlink::security::TrustMutationGeneration;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;

namespace {

class FakePersistence : public ITrustStorePersistence {
  public:
    std::optional<TrustStoreSnapshot> Load() override {
        return TrustStoreSnapshot{};
    }

    bool Save(const TrustStoreSnapshot&) override {
        if (failNextSave_) {
            failNextSave_ = false;
            return false;
        }
        return true;
    }

    void FailNextSave() { failNextSave_ = true; }

  private:
    bool failNextSave_ = false;
};

class BlockingPersistence : public ITrustStorePersistence {
  public:
    std::optional<TrustStoreSnapshot> Load() override {
        return TrustStoreSnapshot{};
    }

    bool Save(const TrustStoreSnapshot&) override {
        std::unique_lock<std::mutex> lock(mutex_);
        entered_ = true;
        enteredCondition_.notify_one();
        releaseCondition_.wait(lock, [this] { return released_; });
        return true;
    }

    void WaitUntilEntered() {
        std::unique_lock<std::mutex> lock(mutex_);
        enteredCondition_.wait(lock, [this] { return entered_; });
    }

    void Release() {
        std::lock_guard<std::mutex> lock(mutex_);
        released_ = true;
        releaseCondition_.notify_one();
    }

  private:
    std::mutex mutex_;
    std::condition_variable enteredCondition_;
    std::condition_variable releaseCondition_;
    bool entered_ = false;
    bool released_ = false;
};

PairingSession::CodeGenerator FixedCode(std::string code) {
    return
        [code = std::move(code)]() -> std::optional<std::string> { return code; };
}

TrustStore::ShortIdGenerator FixedShortId(std::string shortId) {
    return [shortId = std::move(shortId)]() mutable
               -> std::optional<std::string> { return shortId; };
}

std::vector<std::uint8_t> MakeCredential(std::uint8_t seed) {
    return std::vector<std::uint8_t>{seed, static_cast<std::uint8_t>(seed + 1)};
}

void StartPending(PairingSession& pairingSession,
                  TrustMutationGeneration generation,
                  std::chrono::steady_clock::time_point now) {
    REQUIRE(pairingSession.TryStartChallenge("client-1", now).code.has_value());
    REQUIRE(pairingSession
                .TryConfirmCode("123456", now, "client-1", MakeCredential(1),
                                std::nullopt, generation)
                .outcome == dovahlink::security::ConfirmResult::kConfirmed);
}

} //  namespace

TEST_CASE("TrustMutationCoordinator commits pending pairing atomically",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);

    PairingCommitResult result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    REQUIRE(result.outcome == PairingCommitOutcome::kCommitted);
    REQUIRE(result.record.has_value());
    CHECK(result.record->shortId == "11111");
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
    CHECK(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator restores pending pairing after Save failure",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);
    persistence.FailNextSave();

    auto failed =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(failed.outcome == PairingCommitOutcome::kPersistenceFailed);
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
    CHECK_FALSE(trustStore.Query("client-1").has_value());

    auto retried =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);
    CHECK(retried.outcome == PairingCommitOutcome::kCommitted);
}

TEST_CASE("TrustMutationCoordinator rejects a pending pairing after generation changes",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);
    REQUIRE(trustStore.ResetTrust());

    auto result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(result.outcome == PairingCommitOutcome::kPendingNotFound);
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator blocks and cancels the matching pairing",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    REQUIRE(trustStore.Persist("client-1", MakeCredential(9), std::nullopt)
                .has_value());
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);

    CHECK(coordinator.Block("client-1", now) == BlockOutcome::kBlocked);
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
}

TEST_CASE("TrustMutationCoordinator preserves pairing when Block fails",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    REQUIRE(trustStore.Persist("client-1", MakeCredential(9), std::nullopt)
                .has_value());
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);
    persistence.FailNextSave();

    CHECK(coordinator.Block("client-1", now) == BlockOutcome::kSaveFailed);
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
}

TEST_CASE("TrustMutationCoordinator does not cancel for non-mutating Block outcomes",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);

    CHECK(coordinator.Block("unknown", now) == BlockOutcome::kNotFound);
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
}

TEST_CASE("TrustMutationCoordinator reset operations cancel only after Save succeeds",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();

    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);
    persistence.FailNextSave();
    CHECK_FALSE(coordinator.ResetTrust());
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());

    CHECK(coordinator.ResetTrust());
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());

    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);
    persistence.FailNextSave();
    CHECK_FALSE(coordinator.FactoryReset());
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
    CHECK(coordinator.FactoryReset());
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
}

TEST_CASE("TrustMutationCoordinator serializes pairing commit before reset",
          "[application][trust_mutation_coordinator]") {
    BlockingPersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);

    PairingCommitResult commitResult;
    std::thread commitThread([&] {
        commitResult =
            coordinator.CommitPairing("client-1", MakeCredential(1), now);
    });
    persistence.WaitUntilEntered();

    bool resetResult = false;
    std::thread resetThread([&] { resetResult = coordinator.ResetTrust(); });
    persistence.Release();
    commitThread.join();
    resetThread.join();

    CHECK(commitResult.outcome == PairingCommitOutcome::kCommitted);
    CHECK(resetResult);
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator rejects a commit after reset cancels first",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, coordinator.CurrentMutationGeneration(), now);

    REQUIRE(coordinator.ResetTrust());
    auto result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(result.outcome == PairingCommitOutcome::kPendingNotFound);
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

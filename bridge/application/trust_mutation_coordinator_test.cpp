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
#include <type_traits>
#include <vector>

using dovahlink::application::PairingCommitResult;
using dovahlink::application::TrustMutationCoordinator;
using dovahlink::security::BlockOutcome;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::KnownDeviceRecord;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::PairingCommitOutcome;
using dovahlink::security::PairingSession;
using dovahlink::security::TrustMutationGeneration;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;

static_assert(!std::is_default_constructible_v<PairingCommitResult>);
static_assert(!std::is_aggregate_v<PairingCommitResult>);

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

    ///  Re-arms the next save to block until @ref Release is called.
    void BlockNextSave() {
        std::lock_guard<std::mutex> lock(mutex_);
        entered_ = false;
        released_ = false;
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

///  Trust-store short-id generator that returns deterministic candidates in order.
TrustStore::ShortIdGenerator QueuedShortIds(std::vector<std::string> shortIds) {
    return [shortIds = std::move(shortIds)]() mutable
               -> std::optional<std::string> {
        if (shortIds.empty()) {
            return std::nullopt;
        }
        auto result = std::move(shortIds.front());
        shortIds.erase(shortIds.begin());
        return result;
    };
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

TEST_CASE("PairingCommitResult factories preserve their record invariants",
          "[application][pairing_commit_result]") {
    const KnownDeviceRecord record{
        .clientId = "client-1",
        .credential = MakeCredential(1),
        .shortId = "11111",
        .displayName = std::nullopt,
        .state = KnownDeviceState::kTrusted};

    const auto committed = PairingCommitResult::Committed(record);
    REQUIRE(committed.Outcome() == PairingCommitOutcome::kCommitted);
    REQUIRE(committed.Record().has_value());
    CHECK(committed.Record()->clientId == "client-1");

    const auto pendingNotFound = PairingCommitResult::PendingNotFound();
    CHECK(pendingNotFound.Outcome() == PairingCommitOutcome::kPendingNotFound);
    CHECK_FALSE(pendingNotFound.Record().has_value());

    const auto persistenceFailed = PairingCommitResult::PersistenceFailed();
    CHECK(persistenceFailed.Outcome() ==
          PairingCommitOutcome::kPersistenceFailed);
    CHECK_FALSE(persistenceFailed.Record().has_value());

    const auto invalidated = PairingCommitResult::Invalidated();
    CHECK(invalidated.Outcome() == PairingCommitOutcome::kInvalidated);
    CHECK_FALSE(invalidated.Record().has_value());
}

TEST_CASE("TrustMutationCoordinator commits pending pairing atomically",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    PairingCommitResult result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    REQUIRE(result.Outcome() == PairingCommitOutcome::kCommitted);
    REQUIRE(result.Record().has_value());
    CHECK(result.Record()->shortId == "11111");
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
    CHECK(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator captures the client-scoped fence while confirming",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    REQUIRE(trustStore.Persist("client-1", MakeCredential(9), std::nullopt)
                .has_value());
    REQUIRE(trustStore.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(trustStore.Unblock("client-1") ==
            dovahlink::security::UnblockOutcome::kUnblocked);
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();

    REQUIRE(pairingSession.TryStartChallenge("client-1", now).code.has_value());
    auto result = coordinator.ConfirmPairing(
        "123456", now, "client-1", MakeCredential(1), std::nullopt);

    CHECK(result.outcome == dovahlink::security::ConfirmResult::kConfirmed);
    auto pending =
        pairingSession.PeekPending("client-1", MakeCredential(1), now);
    REQUIRE(pending.has_value());
    CHECK(pending->mutationGeneration ==
          trustStore.CurrentMutationGeneration("client-1"));
}

TEST_CASE("TrustMutationCoordinator restores pending pairing after Save failure",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);
    persistence.FailNextSave();

    auto failed =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(failed.Outcome() == PairingCommitOutcome::kPersistenceFailed);
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
    CHECK_FALSE(trustStore.Query("client-1").has_value());

    auto retried =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);
    CHECK(retried.Outcome() == PairingCommitOutcome::kCommitted);
}

TEST_CASE("TrustMutationCoordinator reports an invalidated pending pairing after generation changes",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);
    REQUIRE(trustStore.ResetTrust());

    auto result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(result.Outcome() == PairingCommitOutcome::kInvalidated);
    CHECK_FALSE(result.Record().has_value());
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
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    CHECK(coordinator.Block("client-1", now) == BlockOutcome::kBlocked);
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
}

TEST_CASE("TrustMutationCoordinator preserves a different client's pairing when blocking",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(
        persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(trustStore.Persist("client-2", MakeCredential(9), std::nullopt)
                .has_value());
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    CHECK(coordinator.Block("client-2", now) == BlockOutcome::kBlocked);
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());

    auto result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);
    CHECK(result.Outcome() == PairingCommitOutcome::kCommitted);
    CHECK(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator reports invalidation after a direct Factory Reset",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    REQUIRE(trustStore.Reset());
    auto result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(result.Outcome() == PairingCommitOutcome::kInvalidated);
    CHECK_FALSE(result.Record().has_value());
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
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);
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
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    CHECK(coordinator.Block("unknown", now) == BlockOutcome::kNotFound);
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
}

TEST_CASE("TrustMutationCoordinator revokes and cancels the matching pairing",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    REQUIRE(trustStore.Persist("client-1", MakeCredential(9), std::nullopt)
                .has_value());
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    const auto beforeMutation =
        trustStore.CurrentMutationGeneration("client-1");
    StartPending(pairingSession, beforeMutation, now);

    CHECK(coordinator.Revoke("client-1", now));
    CHECK(trustStore.IsRevoked("client-1"));
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
    CHECK(trustStore.CurrentMutationGeneration("client-1") !=
          beforeMutation);
}

TEST_CASE("TrustMutationCoordinator preserves pairing when Revoke fails",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    REQUIRE(trustStore.Persist("client-1", MakeCredential(9), std::nullopt)
                .has_value());
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    const auto beforeMutation =
        trustStore.CurrentMutationGeneration("client-1");
    StartPending(pairingSession, beforeMutation, now);
    persistence.FailNextSave();

    CHECK_FALSE(coordinator.Revoke("client-1", now));
    CHECK(trustStore.Query("client-1").has_value());
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
    CHECK(trustStore.CurrentMutationGeneration("client-1") ==
          beforeMutation);
}

TEST_CASE("TrustMutationCoordinator does not cancel for a non-mutating Revoke",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    const auto beforeMutation =
        trustStore.CurrentMutationGeneration("client-1");
    StartPending(pairingSession, beforeMutation, now);

    CHECK(coordinator.Revoke("client-1", now));
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
    CHECK(trustStore.CurrentMutationGeneration("client-1") ==
          beforeMutation);
}

TEST_CASE("TrustMutationCoordinator cancels pairing before a concurrent commit when Revoke acquires first",
          "[application][trust_mutation_coordinator]") {
    BlockingPersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    persistence.Release();
    REQUIRE(trustStore.Persist("client-1", MakeCredential(9), std::nullopt)
                .has_value());
    persistence.BlockNextSave();
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession,
                 trustStore.CurrentMutationGeneration("client-1"), now);

    bool revokeResult = false;
    std::thread revokeThread([&] {
        revokeResult = coordinator.Revoke("client-1", now);
    });
    persistence.WaitUntilEntered();

    PairingCommitResult commitResult = PairingCommitResult::PendingNotFound();
    std::thread commitThread([&] {
        commitResult =
            coordinator.CommitPairing("client-1", MakeCredential(1), now);
    });

    persistence.Release();
    revokeThread.join();
    commitThread.join();

    CHECK(revokeResult);
    CHECK(commitResult.Outcome() == PairingCommitOutcome::kPendingNotFound);
    CHECK(trustStore.IsRevoked("client-1"));
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator reset operations cancel only after Save succeeds",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();

    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);
    persistence.FailNextSave();
    CHECK_FALSE(coordinator.ResetTrust().has_value());
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());

    const auto resetResult = coordinator.ResetTrust();
    REQUIRE(resetResult.has_value());
    CHECK(resetResult->empty());
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());

    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);
    persistence.FailNextSave();
    CHECK_FALSE(coordinator.FactoryReset());
    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());
    CHECK(coordinator.FactoryReset());
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
}

TEST_CASE("TrustMutationCoordinator keeps pairing pending until reset persistence succeeds",
          "[application][trust_mutation_coordinator]") {
    BlockingPersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    std::optional<std::vector<std::string>> resetResult;
    std::thread resetThread([&] { resetResult = coordinator.ResetTrust(); });
    persistence.WaitUntilEntered();

    CHECK(pairingSession.PeekPending("client-1", MakeCredential(1), now)
              .has_value());

    persistence.Release();
    resetThread.join();

    REQUIRE(resetResult.has_value());
    CHECK(resetResult->empty());
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
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    PairingCommitResult commitResult = PairingCommitResult::PendingNotFound();
    std::thread commitThread([&] {
        commitResult =
            coordinator.CommitPairing("client-1", MakeCredential(1), now);
    });
    persistence.WaitUntilEntered();

    std::optional<std::vector<std::string>> resetResult;
    std::thread resetThread([&] { resetResult = coordinator.ResetTrust(); });
    persistence.Release();
    commitThread.join();
    resetThread.join();

    CHECK(commitResult.Outcome() == PairingCommitOutcome::kCommitted);
    REQUIRE(resetResult.has_value());
    CHECK(*resetResult == std::vector<std::string>{"client-1"});
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator rejects a commit after reset cancels first",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    REQUIRE(coordinator.ResetTrust().has_value());
    auto result =
        coordinator.CommitPairing("client-1", MakeCredential(1), now);

    CHECK(result.Outcome() == PairingCommitOutcome::kPendingNotFound);
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator rejects a concurrent commit when reset acquires first",
          "[application][trust_mutation_coordinator]") {
    BlockingPersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    std::optional<std::vector<std::string>> resetResult;
    std::thread resetThread([&] { resetResult = coordinator.ResetTrust(); });
    persistence.WaitUntilEntered();

    PairingCommitResult commitResult = PairingCommitResult::PendingNotFound();
    std::thread commitThread([&] {
        commitResult =
            coordinator.CommitPairing("client-1", MakeCredential(1), now);
    });

    persistence.Release();
    resetThread.join();
    commitThread.join();

    REQUIRE(resetResult.has_value());
    CHECK(resetResult->empty());
    CHECK(commitResult.Outcome() == PairingCommitOutcome::kPendingNotFound);
    CHECK_FALSE(trustStore.Query("client-1").has_value());
}

TEST_CASE("TrustMutationCoordinator owns individual and bulk pairing cancellation",
          "[application][trust_mutation_coordinator]") {
    FakePersistence persistence;
    auto trustStore = TrustStore::Load(persistence, FixedShortId("11111"));
    PairingSession pairingSession(FixedCode("123456"));
    TrustMutationCoordinator coordinator(trustStore, pairingSession);
    auto now = std::chrono::steady_clock::now();
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);

    CHECK(coordinator.TryCancel("client-1", now) ==
          dovahlink::security::CancelOutcome::kCancelled);
    StartPending(pairingSession, trustStore.CurrentMutationGeneration("client-1"), now);
    coordinator.CancelAll();
    CHECK_FALSE(pairingSession.PeekPending("client-1", MakeCredential(1), now)
                    .has_value());
}

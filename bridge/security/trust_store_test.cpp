#include "security/trust_store.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cstdint>
#include <deque>
#include <format>
#include <optional>
#include <string>
#include <thread>
#include <vector>

using dovahlink::security::BlockOutcome;
using dovahlink::security::ForgetOutcome;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::KnownDeviceRecord;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::RenameOutcome;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::security::UnblockOutcome;

namespace {

/// In-memory `ITrustStorePersistence` double: never touches real storage, and can be configured
/// to report a corrupt load or a failing save.
class FakePersistence : public ITrustStorePersistence {
public:
    /// Starts with an empty snapshot available to load.
    FakePersistence() : snapshotToLoad_(TrustStoreSnapshot{}) {}

    /// Configures the next `Load()` to report corruption instead of a snapshot.
    void SetCorruptOnLoad() { snapshotToLoad_ = std::nullopt; }

    /// Configures the snapshot `Load()` returns.
    void SetSnapshotToLoad(TrustStoreSnapshot snapshot) { snapshotToLoad_ = std::move(snapshot); }

    /// Configures the next `Save()` call to fail without recording the attempted snapshot.
    void FailNextSave() { failNextSave_ = true; }

    std::optional<TrustStoreSnapshot> Load() override { return snapshotToLoad_; }

    bool Save(const TrustStoreSnapshot&) override {
        ++saveCallCount;
        if (failNextSave_) {
            failNextSave_ = false;
            return false;
        }
        return true;
    }

    /// Number of times `Save()` was invoked, including failed attempts.
    int saveCallCount = 0;

private:
    std::optional<TrustStoreSnapshot> snapshotToLoad_;
    bool failNextSave_ = false;
};

/// Deterministic `TrustStore::ShortIdGenerator` that returns each queued candidate in order.
TrustStore::ShortIdGenerator QueuedShortIds(std::deque<std::optional<std::string>> candidates) {
    auto queue = std::make_shared<std::deque<std::optional<std::string>>>(std::move(candidates));
    return [queue]() -> std::optional<std::string> {
        if (queue->empty()) {
            return std::nullopt;
        }
        auto next = queue->front();
        queue->pop_front();
        return next;
    };
}

/// Builds a deterministic credential-sized byte sequence from a seed value.
std::vector<std::uint8_t> MakeCredential(std::uint8_t seed) {
    return std::vector<std::uint8_t>{seed, static_cast<std::uint8_t>(seed + 1)};
}

}  // namespace

TEST_CASE("a freshly loaded empty store has no trusted clients", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    CHECK_FALSE(store.WasCorruptOnLoad());
    CHECK_FALSE(store.Query("client-1").has_value());
    CHECK(store.ListTrusted().empty());
    CHECK(store.ListAll().empty());
}

TEST_CASE("ListAll returns known devices in every durable state", "[security][trust_store]") {
    FakePersistence persistence;
    auto createdAt = std::chrono::system_clock::time_point(std::chrono::seconds(100));
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{
        .devices = {KnownDeviceRecord{.clientId = "trusted-client",
                                       .credential = MakeCredential(1),
                                       .shortId = "00001",
                                       .displayName = std::string("Trusted"),
                                       .state = KnownDeviceState::kTrusted,
                                       .createdAt = createdAt},
                   KnownDeviceRecord{.clientId = "revoked-client",
                                     .credential = {},
                                     .shortId = "00002",
                                     .displayName = std::nullopt,
                                     .state = KnownDeviceState::kRevoked,
                                     .createdAt = createdAt},
                   KnownDeviceRecord{.clientId = "blocked-client",
                                     .credential = {},
                                     .shortId = "00003",
                                     .displayName = std::string("Blocked"),
                                     .state = KnownDeviceState::kBlocked,
                                     .createdAt = createdAt},
                   KnownDeviceRecord{.clientId = "unpaired-client",
                                     .credential = {},
                                     .shortId = "00004",
                                     .displayName = std::nullopt,
                                     .state = KnownDeviceState::kUnpaired,
                                     .createdAt = createdAt}},
    });

    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    auto records = store.ListAll();

    REQUIRE(records.size() == 4);
    CHECK(std::any_of(records.begin(), records.end(), [](const KnownDeviceRecord& record) {
        return record.state == KnownDeviceState::kTrusted;
    }));
    CHECK(std::any_of(records.begin(), records.end(), [](const KnownDeviceRecord& record) {
        return record.state == KnownDeviceState::kRevoked;
    }));
    CHECK(std::any_of(records.begin(), records.end(), [](const KnownDeviceRecord& record) {
        return record.state == KnownDeviceState::kBlocked;
    }));
    CHECK(std::any_of(records.begin(), records.end(), [](const KnownDeviceRecord& record) {
        return record.state == KnownDeviceState::kUnpaired;
    }));
}

TEST_CASE("loading an existing snapshot makes its records queryable", "[security][trust_store]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{
        .devices = {KnownDeviceRecord{.clientId = "client-1",
                                       .credential = MakeCredential(1),
                                       .shortId = "00001",
                                       .displayName = std::nullopt,
                                       .state = KnownDeviceState::kTrusted,
                                       .createdAt = std::chrono::system_clock::now()}},
    });

    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK_FALSE(store.WasCorruptOnLoad());
    auto record = store.Query("client-1");
    REQUIRE(record.has_value());
    CHECK(record->shortId == "00001");
    CHECK(store.ListTrusted().size() == 1);
}

TEST_CASE("a load reported as corrupt falls back to an empty, non-crashing store",
          "[security][trust_store]") {
    FakePersistence persistence;
    persistence.SetCorruptOnLoad();

    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK(store.WasCorruptOnLoad());
    CHECK(store.ListTrusted().empty());
}

TEST_CASE("Persist assigns the generated shortId and is queryable afterward",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));

    auto result = store.Persist("client-1", MakeCredential(1), std::nullopt);

    REQUIRE(result.has_value());
    CHECK(result->shortId == "12345");
    CHECK(persistence.saveCallCount == 1);
    auto queried = store.Query("client-1");
    REQUIRE(queried.has_value());
    CHECK(queried->credential == MakeCredential(1));
}

TEST_CASE("Persist sets state to kTrusted and stamps createdAt", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));
    auto before = std::chrono::system_clock::now();

    auto result = store.Persist("client-1", MakeCredential(1), std::nullopt);

    auto after = std::chrono::system_clock::now();
    REQUIRE(result.has_value());
    CHECK(result->state == KnownDeviceState::kTrusted);
    CHECK(result->createdAt >= before);
    CHECK(result->createdAt <= after);
}

TEST_CASE("Persist retries shortId generation on collision with a currently trusted shortId",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "11111", "22222"}));

    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    auto second = store.Persist("client-2", MakeCredential(2), std::nullopt);

    REQUIRE(second.has_value());
    CHECK(second->shortId == "22222");
}

TEST_CASE("Persist fails closed once shortId generation attempts are exhausted",
          "[security][trust_store]") {
    FakePersistence persistence;
    std::deque<std::optional<std::string>> candidates;
    for (int i = 0; i < 25; ++i) {
        candidates.push_back(std::string("11111"));
    }
    auto store = TrustStore::Load(persistence, QueuedShortIds(candidates));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    auto second = store.Persist("client-2", MakeCredential(2), std::nullopt);

    CHECK_FALSE(second.has_value());
    CHECK_FALSE(store.Query("client-2").has_value());
}

TEST_CASE("Persist fails closed when the shortId generator itself fails",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({std::nullopt}));

    auto result = store.Persist("client-1", MakeCredential(1), std::nullopt);

    CHECK_FALSE(result.has_value());
}

TEST_CASE("Persist rejects a displayName longer than the configured bound",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));
    std::string tooLong(65, 'a');

    auto result = store.Persist("client-1", MakeCredential(1), tooLong);

    CHECK_FALSE(result.has_value());
    CHECK_FALSE(store.Query("client-1").has_value());
}

TEST_CASE("Persist rejects a displayName containing a control character",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));

    auto result = store.Persist("client-1", MakeCredential(1), std::string("bad\nname"));

    CHECK_FALSE(result.has_value());
}

TEST_CASE("Persist surfaces a Save failure without corrupting in-memory state",
          "[security][trust_store]") {
    FakePersistence persistence;
    // Three candidates: client-1's successful Persist, client-2's failed attempt (the shortId
    // generator is consulted -- and its candidate consumed -- before Save runs, so the failed
    // attempt still burns one), and client-2's successful retry.
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222", "33333"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    persistence.FailNextSave();
    auto failed = store.Persist("client-2", MakeCredential(2), std::nullopt);
    CHECK_FALSE(failed.has_value());
    CHECK_FALSE(store.Query("client-2").has_value());
    CHECK(store.Query("client-1").has_value());

    auto retried = store.Persist("client-2", MakeCredential(2), std::nullopt);
    CHECK(retried.has_value());
}

TEST_CASE("re-pairing a previously revoked clientId returns it to kTrusted",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    REQUIRE(store.IsRevoked("client-1"));

    REQUIRE(store.Persist("client-1", MakeCredential(3), std::nullopt).has_value());

    CHECK_FALSE(store.IsRevoked("client-1"));
}

TEST_CASE("re-pairing a previously revoked clientId reuses its exact shortId and createdAt",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    auto original = store.Persist("client-1", MakeCredential(1), std::nullopt);
    REQUIRE(original.has_value());
    REQUIRE(store.Revoke("client-1"));

    auto repaired = store.Persist("client-1", MakeCredential(3), std::nullopt);

    REQUIRE(repaired.has_value());
    CHECK(repaired->shortId == original->shortId);
    CHECK(repaired->createdAt == original->createdAt);
}

TEST_CASE("re-pairing a previously revoked clientId consumes no new shortId candidate",
          "[security][trust_store]") {
    FakePersistence persistence;
    // Only one candidate is queued for client-1's two Persist calls (initial + re-pair); a second
    // candidate is queued only for client-2, a genuinely new clientId.
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    REQUIRE(store.Persist("client-1", MakeCredential(3), std::nullopt).has_value());

    auto second = store.Persist("client-2", MakeCredential(2), std::nullopt);

    REQUIRE(second.has_value());
    CHECK(second->shortId == "22222");
}

TEST_CASE("Persist still mints a fresh shortId and createdAt for a genuinely new clientId",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    auto second = store.Persist("client-2", MakeCredential(2), std::nullopt);

    REQUIRE(second.has_value());
    CHECK(second->shortId == "22222");
}

TEST_CASE("Revoke on an unknown clientId is a no-op", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK(store.Revoke("never-paired"));

    CHECK_FALSE(store.IsRevoked("never-paired"));
    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Revoke on an already-revoked clientId is a no-op", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    persistence.saveCallCount = 0;

    CHECK(store.Revoke("client-1"));

    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Revoke transitions a trusted client to kRevoked and clears its credential",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK(store.Revoke("client-1"));

    CHECK_FALSE(store.Query("client-1").has_value());
    CHECK(store.IsRevoked("client-1"));
    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(1)));
}

TEST_CASE("a revoked client's shortId stays reserved and cannot be allocated to a different clientId",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));

    // "11111" (client-1's now-revoked shortId) is offered first and must be rejected as a
    // collision even though client-1 is no longer kTrusted; "22222" is the retried candidate.
    auto second = store.Persist("client-2", MakeCredential(2), std::nullopt);

    REQUIRE(second.has_value());
    CHECK(second->shortId == "22222");
}

TEST_CASE("Block transitions a trusted client to kBlocked, clearing its credential and stamping blockedAt",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    auto before = std::chrono::system_clock::now();

    CHECK(store.Block("client-1") == BlockOutcome::kBlocked);

    auto after = std::chrono::system_clock::now();
    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(1)));
    CHECK(store.IsBlocked("client-1"));
    auto found = store.FindByShortId("11111");
    REQUIRE(found.has_value());
    CHECK(found->state == KnownDeviceState::kBlocked);
    REQUIRE(found->blockedAt.has_value());
    CHECK(*found->blockedAt >= before);
    CHECK(*found->blockedAt <= after);
}

TEST_CASE("Block transitions a revoked client to kBlocked", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));

    CHECK(store.Block("client-1") == BlockOutcome::kBlocked);

    CHECK(store.IsBlocked("client-1"));
    CHECK_FALSE(store.IsRevoked("client-1"));
}

TEST_CASE("Block on an unknown clientId reports kNotFound", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK(store.Block("never-paired") == BlockOutcome::kNotFound);
    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Block on an already-blocked clientId reports kAlreadyBlocked and does not save",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    persistence.saveCallCount = 0;

    CHECK(store.Block("client-1") == BlockOutcome::kAlreadyBlocked);

    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Block on an unpaired (unblocked) clientId reports kNotEligible", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    CHECK(store.Block("client-1") == BlockOutcome::kNotEligible);
}

TEST_CASE("Block surfaces a Save failure without corrupting in-memory state", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    persistence.FailNextSave();
    CHECK(store.Block("client-1") == BlockOutcome::kSaveFailed);

    CHECK_FALSE(store.IsBlocked("client-1"));
    CHECK(store.Authenticate("client-1", MakeCredential(1)));
}

TEST_CASE("Persist rejects re-pairing a blocked clientId", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);

    CHECK_FALSE(store.Persist("client-1", MakeCredential(3), std::nullopt).has_value());

    CHECK(store.IsBlocked("client-1"));
    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(3)));
}

TEST_CASE("Unblock transitions a blocked client to kUnpaired, clearing blockedAt and preserving identity",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    auto original = store.Persist("client-1", MakeCredential(1), std::string("My PC"));
    REQUIRE(original.has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);

    CHECK(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    CHECK_FALSE(store.IsBlocked("client-1"));
    auto found = store.FindByShortId("11111");
    REQUIRE(found.has_value());
    CHECK(found->state == KnownDeviceState::kUnpaired);
    CHECK_FALSE(found->blockedAt.has_value());
    CHECK(found->clientId == "client-1");
    CHECK(found->shortId == original->shortId);
    CHECK(found->displayName == original->displayName);
    CHECK(found->createdAt == original->createdAt);
}

TEST_CASE("Unblock does not restore the device's old credential", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(1)));
}

TEST_CASE("Unblock on a trusted clientId reports kNotBlocked", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK(store.Unblock("client-1") == UnblockOutcome::kNotBlocked);
}

TEST_CASE("Unblock on a revoked clientId reports kNotBlocked", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));

    CHECK(store.Unblock("client-1") == UnblockOutcome::kNotBlocked);
}

TEST_CASE("Unblock on an already-unpaired clientId reports kNotBlocked", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    CHECK(store.Unblock("client-1") == UnblockOutcome::kNotBlocked);
}

TEST_CASE("Unblock on an unknown clientId reports kNotFound", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK(store.Unblock("never-paired") == UnblockOutcome::kNotFound);
    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Unblock surfaces a Save failure without corrupting in-memory state", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);

    persistence.FailNextSave();
    CHECK(store.Unblock("client-1") == UnblockOutcome::kSaveFailed);

    CHECK(store.IsBlocked("client-1"));
}

TEST_CASE("IsBlocked distinguishes never-paired, trusted, revoked, unpaired, and blocked clients",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222", "33333", "44444"}));
    REQUIRE(store.Persist("trusted-client", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Persist("revoked-client", MakeCredential(2), std::nullopt).has_value());
    REQUIRE(store.Revoke("revoked-client"));
    REQUIRE(store.Persist("unpaired-client", MakeCredential(3), std::nullopt).has_value());
    REQUIRE(store.Block("unpaired-client") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("unpaired-client") == UnblockOutcome::kUnblocked);
    REQUIRE(store.Persist("blocked-client", MakeCredential(4), std::nullopt).has_value());
    REQUIRE(store.Block("blocked-client") == BlockOutcome::kBlocked);

    CHECK_FALSE(store.IsBlocked("never-paired-client"));
    CHECK_FALSE(store.IsBlocked("trusted-client"));
    CHECK_FALSE(store.IsBlocked("revoked-client"));
    CHECK_FALSE(store.IsBlocked("unpaired-client"));
    CHECK(store.IsBlocked("blocked-client"));
}

TEST_CASE("FindByShortId locates a known device in any state", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222", "33333", "44444"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-2"));
    REQUIRE(store.Persist("client-3", MakeCredential(3), std::nullopt).has_value());
    REQUIRE(store.Block("client-3") == BlockOutcome::kBlocked);
    REQUIRE(store.Persist("client-4", MakeCredential(4), std::nullopt).has_value());
    REQUIRE(store.Block("client-4") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-4") == UnblockOutcome::kUnblocked);

    auto foundTrusted = store.FindByShortId("11111");
    REQUIRE(foundTrusted.has_value());
    CHECK(foundTrusted->clientId == "client-1");

    auto foundRevoked = store.FindByShortId("22222");
    REQUIRE(foundRevoked.has_value());
    CHECK(foundRevoked->clientId == "client-2");

    auto foundBlocked = store.FindByShortId("33333");
    REQUIRE(foundBlocked.has_value());
    CHECK(foundBlocked->clientId == "client-3");

    auto foundUnpaired = store.FindByShortId("44444");
    REQUIRE(foundUnpaired.has_value());
    CHECK(foundUnpaired->clientId == "client-4");
}

TEST_CASE("FindByShortId returns nullopt for an unknown shortId", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK_FALSE(store.FindByShortId("99999").has_value());
}

TEST_CASE("Reset clears every known device regardless of state", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-2"));

    CHECK(store.Reset());

    CHECK(store.ListTrusted().empty());
    CHECK_FALSE(store.IsRevoked("client-2"));
}

TEST_CASE("IsRevoked distinguishes never-paired, currently trusted, and revoked clients",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("trusted-client", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Persist("revoked-client", MakeCredential(2), std::nullopt).has_value());
    REQUIRE(store.Revoke("revoked-client"));

    CHECK_FALSE(store.IsRevoked("never-paired-client"));
    CHECK_FALSE(store.IsRevoked("trusted-client"));
    CHECK(store.IsRevoked("revoked-client"));
}

TEST_CASE("Authenticate succeeds for the matching credential of a trusted client",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK(store.Authenticate("client-1", MakeCredential(1)));
}

TEST_CASE("Authenticate fails for the wrong credential of a real trusted client",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(9)));
}

TEST_CASE("Authenticate fails for an unknown clientId", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK_FALSE(store.Authenticate("never-paired", MakeCredential(1)));
}

TEST_CASE("Authenticate fails for a revoked client even with its former credential",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));

    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(1)));
}

TEST_CASE("Authenticate fails for an empty presented credential", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK_FALSE(store.Authenticate("client-1", std::vector<std::uint8_t>{}));
}

TEST_CASE("Authenticate fails for a non-empty credential of the wrong length",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK_FALSE(store.Authenticate("client-1", std::vector<std::uint8_t>{1, 2, 1}));
}

TEST_CASE("Authenticate succeeds with the new credential after re-pairing a revoked client",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    REQUIRE(store.Persist("client-1", MakeCredential(3), std::nullopt).has_value());

    CHECK(store.Authenticate("client-1", MakeCredential(3)));
    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(1)));
}

TEST_CASE("Persist rejects an empty clientId or an empty credential",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));

    CHECK_FALSE(store.Persist("", MakeCredential(1), std::nullopt).has_value());
    CHECK_FALSE(store.Persist("client-1", std::vector<std::uint8_t>{}, std::nullopt).has_value());
}

TEST_CASE("Persist accepts a valid displayName and it round-trips via Query",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));

    auto result = store.Persist("client-1", MakeCredential(1), std::string("My PC"));

    REQUIRE(result.has_value());
    REQUIRE(result->displayName.has_value());
    CHECK(*result->displayName == "My PC");
    auto queried = store.Query("client-1");
    REQUIRE(queried.has_value());
    REQUIRE(queried->displayName.has_value());
    CHECK(*queried->displayName == "My PC");
}

TEST_CASE("Persist accepts a displayName exactly at the length bound",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));
    std::string atBound(64, 'a');

    auto result = store.Persist("client-1", MakeCredential(1), atBound);

    CHECK(result.has_value());
}

TEST_CASE("Persist rejects a displayName containing a null byte", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));

    auto result = store.Persist("client-1", MakeCredential(1), std::string("bad\0name", 8));

    CHECK_FALSE(result.has_value());
}

TEST_CASE("Persist recovers a corrupt-on-load store into a freshly trusted client",
          "[security][trust_store]") {
    FakePersistence persistence;
    persistence.SetCorruptOnLoad();
    auto store = TrustStore::Load(persistence, QueuedShortIds({"12345"}));
    REQUIRE(store.WasCorruptOnLoad());

    auto result = store.Persist("client-1", MakeCredential(1), std::nullopt);

    REQUIRE(result.has_value());
    CHECK(store.Query("client-1").has_value());
    CHECK(store.ListTrusted().size() == 1);
}

TEST_CASE("Revoke surfaces a Save failure without corrupting in-memory state",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    persistence.FailNextSave();
    CHECK_FALSE(store.Revoke("client-1"));

    CHECK(store.Query("client-1").has_value());
    CHECK_FALSE(store.IsRevoked("client-1"));
}

TEST_CASE("Reset surfaces a Save failure without corrupting in-memory state",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    persistence.FailNextSave();
    CHECK_FALSE(store.Reset());

    CHECK(store.Query("client-1").has_value());
}

TEST_CASE("DefaultShortIdGenerator produces a five-digit numeric candidate",
          "[security][trust_store]") {
    auto candidate = TrustStore::DefaultShortIdGenerator();

    REQUIRE(candidate.has_value());
    CHECK(candidate->size() == 5);
    for (char ch : *candidate) {
        CHECK(std::isdigit(static_cast<unsigned char>(ch)));
    }
}

TEST_CASE("concurrent Persist calls for distinct clients all succeed without data races",
          "[security][trust_store]") {
    // Uses a spin barrier to maximize actual thread overlap rather than a timing sleep to
    // approximate concurrency, matching this project's TokenStore concurrency test.
    FakePersistence persistence;
    std::deque<std::optional<std::string>> candidates;
    constexpr int kClients = 8;
    for (int i = 0; i < kClients; ++i) {
        candidates.push_back(std::format("{:05d}", i));
    }
    auto store = TrustStore::Load(persistence, QueuedShortIds(candidates));

    std::atomic<int> readyCount{0};
    std::atomic<bool> go{false};
    std::atomic<int> successCount{0};
    std::vector<std::thread> threads;
    threads.reserve(kClients);

    for (int i = 0; i < kClients; ++i) {
        threads.emplace_back([&, i]() {
            readyCount.fetch_add(1, std::memory_order_relaxed);
            while (!go.load(std::memory_order_acquire)) {
            }
            auto result = store.Persist("client-" + std::to_string(i), MakeCredential(1),
                                         std::nullopt);
            if (result.has_value()) {
                successCount.fetch_add(1, std::memory_order_relaxed);
            }
        });
    }

    while (readyCount.load(std::memory_order_relaxed) < kClients) {
    }
    go.store(true, std::memory_order_release);

    for (std::thread& t : threads) {
        t.join();
    }

    CHECK(successCount.load() == kClients);
    CHECK(store.ListTrusted().size() == static_cast<std::size_t>(kClients));
}

TEST_CASE("Persist preserves the existing displayName when re-pairing with no displayName supplied",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("My PC")).has_value());
    REQUIRE(store.Revoke("client-1"));

    auto repaired = store.Persist("client-1", MakeCredential(3), std::nullopt);

    REQUIRE(repaired.has_value());
    REQUIRE(repaired->displayName.has_value());
    CHECK(*repaired->displayName == "My PC");
}

TEST_CASE("Persist preserves the existing displayName when re-pairing from unpaired with no "
          "displayName supplied",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("My PC")).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    auto repaired = store.Persist("client-1", MakeCredential(3), std::nullopt);

    REQUIRE(repaired.has_value());
    REQUIRE(repaired->displayName.has_value());
    CHECK(*repaired->displayName == "My PC");
}

TEST_CASE("Persist replaces the existing displayName when re-pairing with a new one supplied",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("My PC")).has_value());
    REQUIRE(store.Revoke("client-1"));

    auto repaired = store.Persist("client-1", MakeCredential(3), std::string("New Name"));

    REQUIRE(repaired.has_value());
    REQUIRE(repaired->displayName.has_value());
    CHECK(*repaired->displayName == "New Name");
}

TEST_CASE("Persist still leaves no displayName for a genuinely new clientId given no displayName",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));

    auto result = store.Persist("client-1", MakeCredential(1), std::nullopt);

    REQUIRE(result.has_value());
    CHECK_FALSE(result->displayName.has_value());
}

TEST_CASE("Rename on an unknown clientId reports kNotFound", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK(store.Rename("never-paired", "New Name") == RenameOutcome::kNotFound);
    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Rename on a revoked clientId reports kNotEligible", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    persistence.saveCallCount = 0;

    CHECK(store.Rename("client-1", "New Name") == RenameOutcome::kNotEligible);

    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Rename on a blocked clientId reports kNotEligible", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    persistence.saveCallCount = 0;

    CHECK(store.Rename("client-1", "New Name") == RenameOutcome::kNotEligible);

    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Rename on an unpaired (unblocked) clientId reports kNotEligible", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);
    persistence.saveCallCount = 0;

    CHECK(store.Rename("client-1", "New Name") == RenameOutcome::kNotEligible);

    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Rename reports kNotEligible ahead of an invalid displayName for an ineligible client",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    std::string tooLong(65, 'a');

    CHECK(store.Rename("client-1", tooLong) == RenameOutcome::kNotEligible);
}

TEST_CASE("Rename replaces a trusted client's displayName", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Old Name")).has_value());

    CHECK(store.Rename("client-1", "New Name") == RenameOutcome::kRenamed);

    auto found = store.Query("client-1");
    REQUIRE(found.has_value());
    REQUIRE(found->displayName.has_value());
    CHECK(*found->displayName == "New Name");
}

TEST_CASE("Rename with an empty displayName clears the name", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Old Name")).has_value());

    CHECK(store.Rename("client-1", "") == RenameOutcome::kRenamed);

    auto found = store.Query("client-1");
    REQUIRE(found.has_value());
    CHECK_FALSE(found->displayName.has_value());
}

TEST_CASE("Rename does not change shortId, createdAt, or credential", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    auto original = store.Persist("client-1", MakeCredential(1), std::string("Old Name"));
    REQUIRE(original.has_value());

    CHECK(store.Rename("client-1", "New Name") == RenameOutcome::kRenamed);

    auto found = store.Query("client-1");
    REQUIRE(found.has_value());
    CHECK(found->shortId == original->shortId);
    CHECK(found->createdAt == original->createdAt);
    CHECK(found->credential == original->credential);
}

TEST_CASE("Rename accepts a displayName exactly at the length bound", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    std::string atBound(64, 'a');

    CHECK(store.Rename("client-1", atBound) == RenameOutcome::kRenamed);

    auto found = store.Query("client-1");
    REQUIRE(found.has_value());
    CHECK(*found->displayName == atBound);
}

TEST_CASE("Rename rejects a displayName longer than the configured bound",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Old Name")).has_value());
    std::string tooLong(65, 'a');

    CHECK(store.Rename("client-1", tooLong) == RenameOutcome::kInvalidDisplayName);

    auto found = store.Query("client-1");
    REQUIRE(found.has_value());
    CHECK(*found->displayName == "Old Name");
}

TEST_CASE("Rename rejects a displayName containing a control character",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK(store.Rename("client-1", std::string("bad\nname")) == RenameOutcome::kInvalidDisplayName);
}

TEST_CASE("Rename rejects a displayName containing a null byte", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK(store.Rename("client-1", std::string("bad\0name", 8)) == RenameOutcome::kInvalidDisplayName);
}

TEST_CASE("Rename surfaces a Save failure without corrupting in-memory state",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Old Name")).has_value());

    persistence.FailNextSave();
    CHECK(store.Rename("client-1", "New Name") == RenameOutcome::kSaveFailed);

    auto found = store.Query("client-1");
    REQUIRE(found.has_value());
    CHECK(*found->displayName == "Old Name");
}

TEST_CASE("Forget on an unknown clientId reports kNotFound", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));

    CHECK(store.Forget("never-paired") == ForgetOutcome::kNotFound);
    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Forget on a trusted clientId reports kNotEligible and leaves it trusted",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());

    CHECK(store.Forget("client-1") == ForgetOutcome::kNotEligible);

    CHECK(store.Query("client-1").has_value());
    CHECK(persistence.saveCallCount == 1);
}

TEST_CASE("Forget on a blocked clientId reports kNotEligible without implicitly lifting the block",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);

    persistence.saveCallCount = 0;
    CHECK(store.Forget("client-1") == ForgetOutcome::kNotEligible);

    CHECK(store.IsBlocked("client-1"));
    CHECK(persistence.saveCallCount == 0);
}

TEST_CASE("Forget deletes a revoked clientId's record entirely", "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));

    CHECK(store.Forget("client-1") == ForgetOutcome::kForgotten);

    CHECK_FALSE(store.IsRevoked("client-1"));
    CHECK_FALSE(store.FindByShortId("11111").has_value());
}

TEST_CASE("Forget deletes an unpaired (unblocked) clientId's record entirely",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    CHECK(store.Forget("client-1") == ForgetOutcome::kForgotten);

    CHECK_FALSE(store.FindByShortId("11111").has_value());
}

TEST_CASE("Forget frees the clientId's shortId for a genuinely new clientId to reuse",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    REQUIRE(store.Forget("client-1") == ForgetOutcome::kForgotten);

    auto result = store.Persist("client-2", MakeCredential(2), std::nullopt);

    REQUIRE(result.has_value());
    CHECK(result->shortId == "11111");
}

TEST_CASE("Forget surfaces a Save failure without corrupting in-memory state (revoked)",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));

    persistence.FailNextSave();
    CHECK(store.Forget("client-1") == ForgetOutcome::kSaveFailed);

    CHECK(store.IsRevoked("client-1"));
    CHECK(store.FindByShortId("11111").has_value());
}

TEST_CASE("Forget surfaces a Save failure without corrupting in-memory state (unpaired)",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);

    persistence.FailNextSave();
    CHECK(store.Forget("client-1") == ForgetOutcome::kSaveFailed);

    auto found = store.FindByShortId("11111");
    REQUIRE(found.has_value());
    CHECK(found->state == KnownDeviceState::kUnpaired);
}

TEST_CASE("re-pairing a clientId after Forget mints a brand-new shortId and createdAt",
          "[security][trust_store]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    auto original = store.Persist("client-1", MakeCredential(1), std::nullopt);
    REQUIRE(original.has_value());
    REQUIRE(store.Revoke("client-1"));
    REQUIRE(store.Forget("client-1") == ForgetOutcome::kForgotten);

    auto repaired = store.Persist("client-1", MakeCredential(3), std::nullopt);

    REQUIRE(repaired.has_value());
    CHECK(repaired->shortId != original->shortId);
    CHECK(repaired->shortId == "22222");
}

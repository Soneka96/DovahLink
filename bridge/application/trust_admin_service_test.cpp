#include "application/trust_admin_service.hpp"

#include "application/active_session_disconnector.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_store.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <cstdint>
#include <deque>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

using dovahlink::application::ActiveSessionDisconnector;
using dovahlink::application::TrustAdminService;
using dovahlink::security::BlockOutcome;
using dovahlink::security::ForgetOutcome;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::PairingSession;
using dovahlink::security::TrustStore;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::security::UnblockOutcome;

namespace {

/// `ActiveSessionDisconnector` double recording every call it receives, so tests can assert exactly
/// which clientId (if any) a successful revoke or reset disconnected. Matches this project's
/// per-file test-double convention (see `FakePersistence` below).
class RecordingSessionDisconnector : public ActiveSessionDisconnector {
public:
    /// Appends `clientId` to `disconnectedClientIds`.
    void DisconnectIfClientActive(std::string_view clientId) override {
        disconnectedClientIds.emplace_back(clientId);
    }

    /// Increments `disconnectActiveCallCount`.
    void DisconnectActive() override { disconnectActiveCallCount++; }

    /// Every clientId passed to `DisconnectIfClientActive`, in order.
    std::vector<std::string> disconnectedClientIds;

    /// How many times `DisconnectActive` was called.
    int disconnectActiveCallCount = 0;
};

/// In-memory `ITrustStorePersistence` double: never touches real storage, and can be configured
/// to report a failing save. Matches this project's per-file test-double convention (see
/// `security/trust_store_test.cpp`'s own `FakePersistence`).
class FakePersistence : public ITrustStorePersistence {
public:
    /// Configures the next `Save()` call to fail.
    void FailNextSave() { failNextSave_ = true; }

    std::optional<TrustStoreSnapshot> Load() override { return TrustStoreSnapshot{}; }

    bool Save(const TrustStoreSnapshot&) override {
        if (failNextSave_) {
            failNextSave_ = false;
            return false;
        }
        return true;
    }

private:
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

TEST_CASE("ListTrusted reports no trusted clients on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ListTrusted() == "No trusted clients.");
}

TEST_CASE("ListTrusted formats one client with a display name, using singular phrasing",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("My Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ListTrusted() == "1 trusted client:\n11111  My Phone");
}

TEST_CASE("ListTrusted shows a placeholder for an absent display name", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ListTrusted() == "1 trusted client:\n11111  (no display name)");
}

TEST_CASE("ListTrusted uses plural phrasing and lists every client", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::string("Tablet")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    std::string listing = service.ListTrusted();
    CHECK(listing.starts_with("2 trusted clients:"));
    CHECK(listing.find("11111  Phone") != std::string::npos);
    CHECK(listing.find("22222  Tablet") != std::string::npos);
    CHECK(disconnector.disconnectedClientIds.empty());
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("RevokeByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.RevokeByShortId("11111") == "No trusted client with id 11111.");
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("ListTrusted mixes a named and an unnamed client in the same listing",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    std::string listing = service.ListTrusted();
    CHECK(listing.find("11111  Phone") != std::string::npos);
    CHECK(listing.find("22222  (no display name)") != std::string::npos);
}

TEST_CASE("RevokeByShortId reports not-found for an unknown shortId without changing the store",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.RevokeByShortId("99999") == "No trusted client with id 99999.");
    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("RevokeByShortId revokes the matching client and reports its display name",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.RevokeByShortId("11111") == "Revoked client 11111 (Phone).");
    CHECK(store.ListTrusted().empty());
    CHECK(store.IsRevoked("client-1"));
    CHECK(disconnector.disconnectedClientIds == std::vector<std::string>{"client-1"});
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("RevokeByShortId only removes the targeted client when two share a display name",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Shared Name")).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::string("Shared Name")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.RevokeByShortId("11111") == "Revoked client 11111 (Shared Name).");
    auto remaining = store.ListTrusted();
    REQUIRE(remaining.size() == 1);
    CHECK(remaining[0].shortId == "22222");
    CHECK(disconnector.disconnectedClientIds == std::vector<std::string>{"client-1"});
}

TEST_CASE("RevokeByShortId surfaces a trust-store save failure and leaves the client trusted",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    persistence.FailNextSave();
    CHECK(service.RevokeByShortId("11111") == "Failed to revoke client 11111: trust-store save failed.");
    CHECK(store.ListTrusted().size() == 1);
    CHECK_FALSE(store.IsRevoked("client-1"));
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("Reset reports zero clients removed on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.Reset() == "Reset all trust (0 clients removed).");
    CHECK(disconnector.disconnectActiveCallCount == 1);
}

TEST_CASE("Reset reports the prior client count and clears the store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::string("Tablet")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.Reset() == "Reset all trust (2 clients removed).");
    CHECK(store.ListTrusted().empty());
    CHECK(disconnector.disconnectActiveCallCount == 1);
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("Reset surfaces a trust-store save failure and leaves clients trusted",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    persistence.FailNextSave();
    CHECK(service.Reset() == "Failed to reset trust: trust-store save failed.");
    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("BlockByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);
    auto now = std::chrono::steady_clock::now();

    CHECK(service.BlockByShortId("11111", now) == "No known device with id 11111.");
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("BlockByShortId blocks a trusted client, disconnects its session, cancels its owned "
          "pairing challenge, and reports its display name",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    auto now = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", now).outcome == dovahlink::security::StartChallengeOutcome::kStarted);
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.BlockByShortId("11111", now) == "Blocked device 11111 (Phone).");

    CHECK(store.IsBlocked("client-1"));
    CHECK_FALSE(store.Authenticate("client-1", MakeCredential(1)));
    CHECK(disconnector.disconnectedClientIds == std::vector<std::string>{"client-1"});
    // The owned challenge was cancelled: a fresh TryStartChallenge for the same clientId starts a
    // brand new one instead of resuming a stale kResumed.
    CHECK(pairingSession.TryStartChallenge("client-1", now).outcome == dovahlink::security::StartChallengeOutcome::kStarted);
}

TEST_CASE("BlockByShortId blocks a revoked client by shortId even though it is not currently trusted",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);
    auto now = std::chrono::steady_clock::now();

    CHECK(service.BlockByShortId("11111", now) == "Blocked device 11111 ((no display name)).");

    CHECK(store.IsBlocked("client-1"));
    CHECK_FALSE(store.IsRevoked("client-1"));
}

TEST_CASE("BlockByShortId reports already-blocked without disconnecting or cancelling again",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);
    auto now = std::chrono::steady_clock::now();

    CHECK(service.BlockByShortId("11111", now) == "Device 11111 is already blocked.");

    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("BlockByShortId reports not-eligible for an unpaired client and cancels no pairing challenge",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == dovahlink::security::UnblockOutcome::kUnblocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    auto now = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", now).outcome == dovahlink::security::StartChallengeOutcome::kStarted);
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.BlockByShortId("11111", now) == "Device 11111 cannot be blocked (not currently trusted or revoked).");

    // A non-kBlocked outcome must not have touched the owned challenge: it is still resumable, not
    // cancelled.
    CHECK(pairingSession.TryStartChallenge("client-1", now).outcome == dovahlink::security::StartChallengeOutcome::kResumed);
}

TEST_CASE("BlockByShortId surfaces a trust-store save failure and leaves the client trusted",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);
    auto now = std::chrono::steady_clock::now();

    persistence.FailNextSave();
    CHECK(service.BlockByShortId("11111", now) == "Failed to block device 11111: trust-store save failed.");

    CHECK_FALSE(store.IsBlocked("client-1"));
    CHECK(store.Authenticate("client-1", MakeCredential(1)));
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("UnblockByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.UnblockByShortId("11111") == "No known device with id 11111.");
}

TEST_CASE("UnblockByShortId unblocks a blocked client and reports its display name",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.UnblockByShortId("11111") == "Unblocked device 11111 (Phone).");

    CHECK_FALSE(store.IsBlocked("client-1"));
    // Unblock never force-closes a session -- a blocked device has none to disconnect.
    CHECK(disconnector.disconnectedClientIds.empty());
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("UnblockByShortId reports not-blocked for a trusted client", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.UnblockByShortId("11111") == "Device 11111 is not blocked.");
}

TEST_CASE("UnblockByShortId surfaces a trust-store save failure and leaves the client blocked",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    persistence.FailNextSave();
    CHECK(service.UnblockByShortId("11111") == "Failed to unblock device 11111: trust-store save failed.");

    CHECK(store.IsBlocked("client-1"));
}

TEST_CASE("RevokeByShortId reports not-found for a blocked device, mirroring an unknown shortId",
          "[application][trust_admin_service]") {
    // RevokeByShortId only ever finds a currently-kTrusted device (via TrustStore::ListTrusted); a
    // blocked device is invisible to it, exactly like a shortId that was never allocated at all.
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.RevokeByShortId("11111") == "No trusted client with id 11111.");

    CHECK(store.IsBlocked("client-1"));
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("ForgetByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ForgetByShortId("11111") == "No known device with id 11111.");
}

TEST_CASE("ForgetByShortId reports not-eligible for a trusted client and leaves it trusted",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ForgetByShortId("11111") == "Device 11111 cannot be forgotten (revoke or unblock it first).");

    CHECK(store.Query("client-1").has_value());
}

TEST_CASE("ForgetByShortId reports not-eligible for a blocked client without lifting the block",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ForgetByShortId("11111") == "Device 11111 cannot be forgotten (revoke or unblock it first).");

    CHECK(store.IsBlocked("client-1"));
}

TEST_CASE("ForgetByShortId forgets a revoked client and reports its display name",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Revoke("client-1"));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ForgetByShortId("11111") == "Forgot device 11111 (Phone).");

    CHECK_FALSE(store.FindByShortId("11111").has_value());
}

TEST_CASE("ForgetByShortId forgets an unpaired (unblocked) client", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Block("client-1") == BlockOutcome::kBlocked);
    REQUIRE(store.Unblock("client-1") == UnblockOutcome::kUnblocked);
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ForgetByShortId("11111") == "Forgot device 11111 ((no display name)).");

    CHECK_FALSE(store.FindByShortId("11111").has_value());
}

TEST_CASE("ForgetByShortId surfaces a trust-store save failure and leaves the record intact",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    persistence.FailNextSave();
    CHECK(service.ForgetByShortId("11111") == "Failed to forget device 11111: trust-store save failed.");

    CHECK(store.FindByShortId("11111").has_value());
}

TEST_CASE("ForgetByShortId only erases the targeted client when two share a display name, leaving "
          "the other device untouched",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Shared Name")).has_value());
    REQUIRE(store.Revoke("client-1"));
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::string("Shared Name")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);

    CHECK(service.ForgetByShortId("11111") == "Forgot device 11111 (Shared Name).");

    CHECK_FALSE(store.FindByShortId("11111").has_value());
    auto remaining = store.FindByShortId("22222");
    REQUIRE(remaining.has_value());
    CHECK(remaining->clientId == "client-2");
    CHECK(remaining->state == KnownDeviceState::kTrusted);
}

TEST_CASE("ForgetByShortId reports not-found when re-forgetting an already-forgotten shortId",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Revoke("client-1"));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    TrustAdminService service(store, disconnector, pairingSession);
    REQUIRE(service.ForgetByShortId("11111") == "Forgot device 11111 ((no display name)).");

    CHECK(service.ForgetByShortId("11111") == "No known device with id 11111.");
}

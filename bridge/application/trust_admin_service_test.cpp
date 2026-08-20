#include "application/trust_admin_service.hpp"

#include "application/active_session_disconnector.hpp"
#include "security/factory_reset_challenge.hpp"
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
using dovahlink::security::FactoryResetChallenge;
using dovahlink::security::ForgetOutcome;
using dovahlink::security::ITrustStorePersistence;
using dovahlink::security::KnownDeviceState;
using dovahlink::security::KnownDeviceRecord;
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
    void DisconnectIfClientActive(std::string_view clientId, std::string_view /*reason*/) override {
        disconnectedClientIds.emplace_back(clientId);
    }

    /// Increments `disconnectActiveCallCount`.
    void DisconnectActive(std::string_view /*reason*/) override { disconnectActiveCallCount++; }

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

    /// Configures the snapshot returned by `Load()`.
    void SetSnapshotToLoad(TrustStoreSnapshot snapshot) { snapshotToLoad_ = std::move(snapshot); }

    std::optional<TrustStoreSnapshot> Load() override { return snapshotToLoad_; }

    bool Save(const TrustStoreSnapshot&) override {
        if (failNextSave_) {
            failNextSave_ = false;
            return false;
        }
        return true;
    }

private:
    std::optional<TrustStoreSnapshot> snapshotToLoad_ = TrustStoreSnapshot{};
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

/// Builds a known-device record with a deterministic creation time for ordering assertions.
KnownDeviceRecord MakeKnownDevice(std::string clientId, std::string shortId,
                                  std::optional<std::string> displayName, KnownDeviceState state,
                                  int createdAtSeconds) {
    return KnownDeviceRecord{.clientId = std::move(clientId),
                             .credential = state == KnownDeviceState::kTrusted ? MakeCredential(1)
                                                                                 : std::vector<std::uint8_t>{},
                             .shortId = std::move(shortId),
                             .displayName = std::move(displayName),
                             .state = state,
                             .createdAt = std::chrono::system_clock::time_point(
                                 std::chrono::seconds(createdAtSeconds))};
}

/// Deterministic `FactoryResetChallenge::CodeGenerator` that always returns `code`.
FactoryResetChallenge::CodeGenerator FixedFactoryResetCode(std::string code) {
    return [code = std::move(code)]() -> std::optional<std::string> { return code; };
}

/// `FactoryResetChallenge::CodeGenerator` that always fails, simulating a CSPRNG failure.
FactoryResetChallenge::CodeGenerator FailingFactoryResetCode() {
    return []() -> std::optional<std::string> { return std::nullopt; };
}

/// Deterministic `FactoryResetChallenge::CodeGenerator` that returns each queued code in order.
FactoryResetChallenge::CodeGenerator QueuedFactoryResetCodes(std::deque<std::string> codes) {
    auto queue = std::make_shared<std::deque<std::string>>(std::move(codes));
    return [queue]() -> std::optional<std::string> {
        REQUIRE_FALSE(queue->empty());
        auto next = queue->front();
        queue->pop_front();
        return next;
    };
}

}  // namespace

TEST_CASE("ListTrusted reports no trusted clients on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ListTrusted() == "No trusted clients.");
}

TEST_CASE("ListTrusted formats one client with a display name, using singular phrasing",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("My Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ListTrusted() == "1 trusted client:\n11111  My Phone");
}

TEST_CASE("ListTrusted shows a placeholder for an absent display name", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ListTrusted() == "1 trusted client:\n11111  (no display name)");
}

TEST_CASE("ListTrusted uses plural phrasing and lists every client", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::string("Tablet")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    std::string listing = service.ListTrusted();
    CHECK(listing.starts_with("2 trusted clients:"));
    CHECK(listing.find("11111  Phone") != std::string::npos);
    CHECK(listing.find("22222  Tablet") != std::string::npos);
    CHECK(disconnector.disconnectedClientIds.empty());
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("ListTrusted disambiguates duplicate display names oldest first",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-2", "22222", std::string("Shared"), KnownDeviceState::kTrusted, 100),
        MakeKnownDevice("client-1", "11111", std::string("Shared"), KnownDeviceState::kTrusted, 100),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ListTrusted() == "2 trusted clients:\n11111  Shared #1\n22222  Shared #2");
}

TEST_CASE("List scopes select all trusted and blocked devices", "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-1", "11111", std::string("Trusted"), KnownDeviceState::kTrusted, 100),
        MakeKnownDevice("client-2", "22222", std::string("Revoked"), KnownDeviceState::kRevoked, 200),
        MakeKnownDevice("client-3", "33333", std::string("Blocked"), KnownDeviceState::kBlocked, 300),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.List("") == service.ListKnownDevices());
    CHECK(service.List("all") == service.ListKnownDevices());
    CHECK(service.List("All") == service.ListKnownDevices());

    const std::string trusted = service.List("trust");
    CHECK(trusted == service.ListTrusted());
    CHECK(service.List("TRUST") == trusted);
    CHECK(trusted.find("11111  Trusted") != std::string::npos);
    CHECK(trusted.find("22222") == std::string::npos);
    CHECK(trusted.find("33333") == std::string::npos);

    const std::string blocked = service.List("block");
    CHECK(blocked == service.ListBlocked());
    CHECK(service.List("Block") == blocked);
    CHECK(blocked.find("33333  Blocked  blocked") != std::string::npos);
    CHECK(blocked.find("11111") == std::string::npos);
    CHECK(blocked.find("22222") == std::string::npos);
}

TEST_CASE("List rejects an unknown scope with the canonical choices", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.List("revoked") == "Unknown list scope 'revoked'. Use all, trust, or block.");
}

TEST_CASE("Help lists only the canonical trust-admin commands", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.Help() ==
          "DovahLink commands:\n"
          " list [all|trust|block]\n"
          " revoke -id <id> | block -id <id> | unblock -id <id> | forget -id <id>\n"
          " reset trust | reset | reset -confirm <code> | help");
}

TEST_CASE("RevokeByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    std::string listing = service.ListTrusted();
    CHECK(listing.find("11111  Phone") != std::string::npos);
    CHECK(listing.find("22222  (no display name)") != std::string::npos);
}

TEST_CASE("ListKnownDevices and ListBlocked report clear empty results",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ListKnownDevices() == "No known devices.");
    CHECK(service.ListBlocked() == "No blocked devices.");
}

TEST_CASE("ListKnownDevices formats a single device with its state",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-1", "11111", std::string("Phone"), KnownDeviceState::kTrusted, 100),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ListKnownDevices() == "1 known device:\n11111  Phone  trusted");
}

TEST_CASE("ListKnownDevices keeps distinct display names unsuffixed and sorts by creation time",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-2", "22222", std::string("Tablet"), KnownDeviceState::kRevoked, 200),
        MakeKnownDevice("client-1", "11111", std::string("Phone"), KnownDeviceState::kTrusted, 100),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    const std::string listing = service.ListKnownDevices();
    const auto first = listing.find("11111  Phone  trusted");
    const auto second = listing.find("22222  Tablet  revoked");
    REQUIRE(first != std::string::npos);
    REQUIRE(second != std::string::npos);
    CHECK(first < second);
    CHECK(listing.find("#") == std::string::npos);
}

TEST_CASE("ListKnownDevices disambiguates two and three duplicate names oldest first",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-3", "33333", std::string("Shared"), KnownDeviceState::kUnpaired, 300),
        MakeKnownDevice("client-1", "11111", std::string("Shared"), KnownDeviceState::kRevoked, 100),
        MakeKnownDevice("client-2", "22222", std::string("Shared"), KnownDeviceState::kBlocked, 200),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    const std::string listing = service.ListKnownDevices();
    const auto first = listing.find("11111  Shared #1  revoked");
    const auto second = listing.find("22222  Shared #2  blocked");
    const auto third = listing.find("33333  Shared #3  unpaired");
    REQUIRE(first != std::string::npos);
    REQUIRE(second != std::string::npos);
    REQUIRE(third != std::string::npos);
    CHECK(first < second);
    CHECK(second < third);
}

TEST_CASE("ListKnownDevices recomputes duplicate suffixes after forgetting the oldest device",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-2", "22222", std::string("Shared"), KnownDeviceState::kRevoked, 200),
        MakeKnownDevice("client-1", "11111", std::string("Shared"), KnownDeviceState::kRevoked, 100),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    REQUIRE(service.ListKnownDevices().find("11111  Shared #1  revoked") != std::string::npos);
    REQUIRE(service.ListKnownDevices().find("22222  Shared #2  revoked") != std::string::npos);
    CHECK(service.ForgetByShortId("11111") == "Forgot device 11111 (Shared).");
    CHECK(service.ListKnownDevices() == "1 known device:\n22222  Shared  revoked");
}

TEST_CASE("ListBlocked includes blocked devices while excluding nonblocked devices",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    persistence.SetSnapshotToLoad(TrustStoreSnapshot{.devices = {
        MakeKnownDevice("client-1", "11111", std::string("Phone"), KnownDeviceState::kTrusted, 100),
        MakeKnownDevice("client-2", "22222", std::string("Blocked Phone"), KnownDeviceState::kBlocked, 200),
        MakeKnownDevice("client-3", "33333", std::nullopt, KnownDeviceState::kRevoked, 300),
        MakeKnownDevice("client-4", "44444", std::string("Blocked Phone"), KnownDeviceState::kBlocked, 150),
    }});
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    const std::string all = service.ListKnownDevices();
    const std::string blocked = service.ListBlocked();
    CHECK(all.find("11111  Phone  trusted") != std::string::npos);
    CHECK(all.find("22222  Blocked Phone #2  blocked") != std::string::npos);
    CHECK(all.find("33333  (no display name)  revoked") != std::string::npos);
    CHECK(blocked == "2 blocked devices:\n44444  Blocked Phone #1  blocked\n22222  Blocked Phone #2  blocked");
}

TEST_CASE("RevokeByShortId reports not-found for an unknown shortId without changing the store",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    persistence.FailNextSave();
    CHECK(service.RevokeByShortId("11111") == "Failed to revoke client 11111: trust-store save failed.");
    CHECK(store.ListTrusted().size() == 1);
    CHECK_FALSE(store.IsRevoked("client-1"));
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("StartFactoryReset returns a message containing the generated code and mutates nothing",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge(FixedFactoryResetCode("654321"));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.StartFactoryReset() ==
          "Factory Reset requested. Confirm with code 654321 within 60 seconds to permanently erase all trust.");

    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("StartFactoryReset reports a generator failure", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge(FailingFactoryResetCode());
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.StartFactoryReset() == "Failed to start Factory Reset: could not generate a confirmation code.");
}

TEST_CASE("ConfirmFactoryReset with the correct code wipes trust, cancels pairing, and disconnects",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::string("Phone")).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::string("Tablet")).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    REQUIRE(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
            dovahlink::security::StartChallengeOutcome::kStarted);
    FactoryResetChallenge factoryResetChallenge(FixedFactoryResetCode("654321"));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.StartFactoryReset().find("654321") != std::string::npos);

    CHECK(service.ConfirmFactoryReset("654321") == "Factory Reset complete (2 trusted devices erased).");

    CHECK(store.ListTrusted().empty());
    CHECK(store.ListAll().empty());
    CHECK(disconnector.disconnectActiveCallCount == 1);
    CHECK(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
          dovahlink::security::StartChallengeOutcome::kStarted);
}

TEST_CASE("ConfirmFactoryReset with the wrong code performs no destructive change",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge(FixedFactoryResetCode("654321"));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.StartFactoryReset().find("654321") != std::string::npos);

    CHECK(service.ConfirmFactoryReset("000000") ==
          "Wrong Factory Reset confirmation code; the challenge was cancelled. Start over with 'reset'.");

    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("ConfirmFactoryReset with no challenge started performs no destructive change",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ConfirmFactoryReset("000000") ==
          "No Factory Reset confirmation is pending; start one with 'reset' first.");

    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectActiveCallCount == 0);
}

TEST_CASE("ConfirmFactoryReset surfaces a trust-store save failure, leaves clients trusted, and "
          "cancels no pairing",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    REQUIRE(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
            dovahlink::security::StartChallengeOutcome::kStarted);
    FactoryResetChallenge factoryResetChallenge(FixedFactoryResetCode("654321"));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.StartFactoryReset().find("654321") != std::string::npos);

    persistence.FailNextSave();
    CHECK(service.ConfirmFactoryReset("654321") == "Failed to complete Factory Reset: trust-store save failed.");

    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectActiveCallCount == 0);
    // The still-owned challenge proves CancelAll was never reached after the save failure.
    CHECK(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
          dovahlink::security::StartChallengeOutcome::kResumed);
}

TEST_CASE("ConfirmFactoryReset called again after a successful confirm reports no challenge pending",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge(FixedFactoryResetCode("654321"));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.StartFactoryReset().find("654321") != std::string::npos);
    REQUIRE(service.ConfirmFactoryReset("654321") == "Factory Reset complete (1 trusted device erased).");

    CHECK(service.ConfirmFactoryReset("654321") ==
          "No Factory Reset confirmation is pending; start one with 'reset' first.");
}

TEST_CASE("StartFactoryReset called twice invalidates the first code",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge(QueuedFactoryResetCodes({"111111", "222222"}));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.StartFactoryReset().find("111111") != std::string::npos);

    REQUIRE(service.StartFactoryReset().find("222222") != std::string::npos);

    CHECK(service.ConfirmFactoryReset("111111") ==
          "Wrong Factory Reset confirmation code; the challenge was cancelled. Start over with 'reset'.");
    CHECK(store.ListTrusted().size() == 1);
}

TEST_CASE("ConfirmFactoryReset reports zero trusted devices erased and singular phrasing for one",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge(FixedFactoryResetCode("654321"));
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.StartFactoryReset().find("654321") != std::string::npos);

    CHECK(service.ConfirmFactoryReset("654321") == "Factory Reset complete (0 trusted devices erased).");
}

TEST_CASE("ResetTrust revokes every trusted device, cancels pairing, and disconnects",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111", "22222"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    REQUIRE(store.Persist("client-2", MakeCredential(2), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    REQUIRE(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
            dovahlink::security::StartChallengeOutcome::kStarted);
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ResetTrust() == "Reset Trust complete (2 devices revoked).");

    CHECK(store.ListTrusted().empty());
    CHECK(store.IsRevoked("client-1"));
    CHECK(store.IsRevoked("client-2"));
    CHECK(disconnector.disconnectActiveCallCount == 1);
    CHECK(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
          dovahlink::security::StartChallengeOutcome::kStarted);
}

TEST_CASE("ResetTrust reports zero devices revoked on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ResetTrust() == "Reset Trust complete (0 devices revoked).");
    CHECK(disconnector.disconnectActiveCallCount == 1);
}

TEST_CASE("ResetTrust surfaces a trust-store save failure, leaves clients trusted, and cancels no "
          "pairing",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    REQUIRE(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
            dovahlink::security::StartChallengeOutcome::kStarted);
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    persistence.FailNextSave();
    CHECK(service.ResetTrust() == "Failed to reset trust: trust-store save failed.");
    CHECK(store.ListTrusted().size() == 1);
    CHECK(disconnector.disconnectActiveCallCount == 0);
    // The still-owned challenge proves CancelAll was never reached after the save failure.
    CHECK(pairingSession.TryStartChallenge("other-client", std::chrono::steady_clock::now()).outcome ==
          dovahlink::security::StartChallengeOutcome::kResumed);
}

TEST_CASE("BlockByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
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
    FactoryResetChallenge factoryResetChallenge;
    auto now = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", now).outcome == dovahlink::security::StartChallengeOutcome::kStarted);
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
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
    FactoryResetChallenge factoryResetChallenge;
    auto now = std::chrono::steady_clock::now();
    REQUIRE(pairingSession.TryStartChallenge("client-1", now).outcome == dovahlink::security::StartChallengeOutcome::kStarted);
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.RevokeByShortId("11111") == "No trusted client with id 11111.");

    CHECK(store.IsBlocked("client-1"));
    CHECK(disconnector.disconnectedClientIds.empty());
}

TEST_CASE("ForgetByShortId reports not-found on an empty store", "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({}));
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

    CHECK(service.ForgetByShortId("11111") == "No known device with id 11111.");
}

TEST_CASE("ForgetByShortId reports not-eligible for a trusted client and leaves it trusted",
          "[application][trust_admin_service]") {
    FakePersistence persistence;
    auto store = TrustStore::Load(persistence, QueuedShortIds({"11111"}));
    REQUIRE(store.Persist("client-1", MakeCredential(1), std::nullopt).has_value());
    RecordingSessionDisconnector disconnector;
    PairingSession pairingSession;
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);

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
    FactoryResetChallenge factoryResetChallenge;
    TrustAdminService service(store, disconnector, pairingSession, factoryResetChallenge);
    REQUIRE(service.ForgetByShortId("11111") == "Forgot device 11111 ((no display name)).");

    CHECK(service.ForgetByShortId("11111") == "No known device with id 11111.");
}

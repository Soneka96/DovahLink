#pragma once

#include "security/i_trust_store_persistence.hpp"
#include "security/known_device_record.hpp"
#include "security/trust_store_snapshot.hpp"

#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

namespace dovahlink::security {

/// Thread-safe persistent-trust domain service: load, persist, revoke, reset, query. Fails closed
/// on corrupt or inaccessible persistence -- it never crashes, never silently trusts a client, and
/// always supports a clean reset-and-re-pair path. `KnownDeviceRecord::displayName`'s bound and
/// character invariants are enforced only by `Persist`, the sole production construction path;
/// records obtained any other way are trusted as already valid.
class TrustStore {
public:
    /// Produces one five-digit `shortId` candidate, or `std::nullopt` when the underlying random
    /// source fails. `Persist` retries this on collision with a currently trusted `shortId`.
    using ShortIdGenerator = std::function<std::optional<std::string>()>;

    /// Loads a `TrustStore` from `persistence`, falling back to an empty, non-corrupt store when
    /// `persistence.Load()` reports corruption or inaccessibility; `WasCorruptOnLoad()` reports
    /// which happened.
    /// @param persistence Storage backend that outlives this store.
    /// @param shortIdGenerator Produces each five-digit `shortId` candidate `Persist` tries;
    ///     defaults to the real CSPRNG-backed generator. Tests inject a deterministic or
    ///     collision-forcing generator here.
    [[nodiscard]] static TrustStore Load(ITrustStorePersistence& persistence,
                                          ShortIdGenerator shortIdGenerator = DefaultShortIdGenerator);

    /// Generates one five-digit `shortId` candidate using the system CSPRNG.
    [[nodiscard]] static std::optional<std::string> DefaultShortIdGenerator();

    /// Reports whether the most recent load found persistence corrupt or inaccessible and fell
    /// back to an empty store.
    [[nodiscard]] bool WasCorruptOnLoad() const noexcept;

    /// Returns the known device record for `clientId`, if it is currently in the `kTrusted` state.
    [[nodiscard]] std::optional<KnownDeviceRecord> Query(const std::string& clientId);

    /// Returns every currently trusted (`kTrusted`) device, for administration listings.
    [[nodiscard]] std::vector<KnownDeviceRecord> ListTrusted();

    /// Reports whether `clientId` is a known device currently in the `kRevoked` state.
    [[nodiscard]] bool IsRevoked(const std::string& clientId);

    /// Reports whether `presentedCredential` matches the credential currently trusted for
    /// `clientId`, using a constant-time comparison. `false` for an unknown clientId, a clientId
    /// not currently `kTrusted`, or an empty `presentedCredential`.
    [[nodiscard]] bool Authenticate(const std::string& clientId,
                                     const std::vector<std::uint8_t>& presentedCredential);

    /// Binds `credential` to `clientId` and transitions it to `kTrusted`. When `clientId` already
    /// has a known device record (in any prior state), reuses its existing `shortId` and
    /// `createdAt` -- re-pairing never changes a device's identity or mints a second `shortId` for
    /// it. Otherwise assigns a newly generated unique `shortId` and a fresh `createdAt`. Rejects an
    /// empty `clientId` or `credential`, and rejects a `displayName` that exceeds the configured
    /// length bound or contains a control character.
    /// @return The new record, or `std::nullopt` when validation, `shortId` generation, or the
    ///     underlying `Save` fails. On any failure the in-memory state is left unchanged.
    [[nodiscard]] std::optional<KnownDeviceRecord> Persist(
        std::string clientId, std::vector<std::uint8_t> credential,
        std::optional<std::string> displayName);

    /// Transitions a currently `kTrusted` `clientId` to `kRevoked`, securely clearing its
    /// credential while keeping its record (identity, `shortId`, `displayName`, `createdAt`). A
    /// `clientId` that is unknown or already not `kTrusted` is a no-op.
    /// @return Whether the underlying `Save` succeeded. On failure the in-memory state is left
    ///     unchanged.
    [[nodiscard]] bool Revoke(const std::string& clientId);

    /// Removes every known device record, securely clearing every removed credential.
    /// @return Whether the underlying `Save` succeeded. On failure the in-memory state is left
    ///     unchanged.
    [[nodiscard]] bool Reset();

private:
    /// Constructs a store from already-loaded state.
    TrustStore(ITrustStorePersistence& persistence, ShortIdGenerator shortIdGenerator,
               TrustStoreSnapshot snapshot, bool wasCorruptOnLoad);

    /// Generates a `shortId` not currently used by any known device record in any state, retrying
    /// on collision up to a bounded attempt count -- a `shortId` stays reserved for as long as its
    /// record exists, regardless of that record's current state.
    /// @return The generated `shortId`, or `std::nullopt` on generator failure or attempt
    ///     exhaustion.
    [[nodiscard]] std::optional<std::string> GenerateUniqueShortId();

    /// Reports whether `displayName` satisfies the length bound and is free of control
    /// characters.
    [[nodiscard]] static bool IsValidDisplayName(const std::string& displayName);

    /// Builds the current in-memory state into a snapshot suitable for `Save`.
    [[nodiscard]] TrustStoreSnapshot BuildSnapshot() const;

    /// Storage backend this store reads from and writes to.
    ITrustStorePersistence* persistence_;

    /// Produces each five-digit `shortId` candidate.
    ShortIdGenerator shortIdGenerator_;

    /// Serializes access to in-memory trust state.
    std::mutex mutex_;

    /// Every known device, keyed by `clientId`, regardless of state.
    std::unordered_map<std::string, KnownDeviceRecord> devices_;

    /// Whether the most recent load fell back to empty due to corrupt/inaccessible persistence.
    bool wasCorruptOnLoad_;
};

}  // namespace dovahlink::security

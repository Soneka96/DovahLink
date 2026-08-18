#pragma once

#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace dovahlink::security {

/// One persistently trusted client's credential and administration metadata.
struct TrustedClientRecord {
    /// The client's stable protocol identity.
    std::string clientId;
    /// The strong device-scoped credential bound to `clientId`.
    std::vector<std::uint8_t> credential;
    /// Five-digit administration-only identifier, unique among currently trusted clients. Never
    /// authentication or authorization material.
    std::string shortId;
    /// Optional bounded, control-character-free, presentation-only label. Never authentication or
    /// authorization material and never a substitute for `clientId`.
    std::optional<std::string> displayName;
};

/// Marks a `clientId` as intentionally revoked without retaining its credential.
struct RevocationTombstone {
    /// The revoked client's stable protocol identity.
    std::string clientId;
};

/// The durable state a `TrustStore` loads from and saves to persistent storage.
struct TrustStoreSnapshot {
    /// Currently trusted clients.
    std::vector<TrustedClientRecord> records;
    /// Clients explicitly revoked and not yet re-paired.
    std::vector<RevocationTombstone> tombstones;
};

/// Persistence boundary a `TrustStore` reads from and writes to. Implementations own the actual
/// storage mechanism (for example per-user secure storage); `TrustStore` never assumes a file
/// format or storage location.
class ITrustStorePersistence {
public:
    /// Allows derived storage backends to be destroyed through this interface.
    virtual ~ITrustStorePersistence() = default;

    /// Loads the durable trust snapshot, or `std::nullopt` when storage is corrupt or
    /// inaccessible. A missing-but-valid empty store is a successful load of an empty snapshot,
    /// not a `std::nullopt` result.
    [[nodiscard]] virtual std::optional<TrustStoreSnapshot> Load() = 0;

    /// Durably saves the given snapshot.
    /// @return Whether the save succeeded.
    [[nodiscard]] virtual bool Save(const TrustStoreSnapshot& snapshot) = 0;
};

/// Thread-safe persistent-trust domain service: load, persist, revoke, reset, query. Fails closed
/// on corrupt or inaccessible persistence -- it never crashes, never silently trusts a client, and
/// always supports a clean reset-and-re-pair path. `TrustedClientRecord::displayName`'s bound and
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

    /// Returns the trusted client record for `clientId`, if currently trusted.
    [[nodiscard]] std::optional<TrustedClientRecord> Query(const std::string& clientId);

    /// Returns every currently trusted client, for administration listings.
    [[nodiscard]] std::vector<TrustedClientRecord> ListTrusted();

    /// Reports whether `clientId` was explicitly revoked and has not been re-paired since.
    [[nodiscard]] bool IsRevoked(const std::string& clientId);

    /// Reports whether `presentedCredential` matches the credential currently trusted for
    /// `clientId`, using a constant-time comparison. `false` for an unknown or revoked `clientId`,
    /// or an empty `presentedCredential`.
    [[nodiscard]] bool Authenticate(const std::string& clientId,
                                     const std::vector<std::uint8_t>& presentedCredential);

    /// Binds `credential` to `clientId`, assigning a unique `shortId` and clearing any revocation
    /// tombstone for `clientId`. Rejects an empty `clientId` or `credential`, and rejects a
    /// `displayName` that exceeds the configured length bound or contains a control character.
    /// @return The new record, or `std::nullopt` when validation, `shortId` generation, or the
    ///     underlying `Save` fails. On any failure the in-memory state is left unchanged.
    [[nodiscard]] std::optional<TrustedClientRecord> Persist(
        std::string clientId, std::vector<std::uint8_t> credential,
        std::optional<std::string> displayName);

    /// Removes `clientId`'s trust and records a revocation tombstone, securely clearing the
    /// removed credential. A `clientId` that was never trusted is a no-op.
    /// @return Whether the underlying `Save` succeeded. On failure the in-memory state is left
    ///     unchanged.
    [[nodiscard]] bool Revoke(const std::string& clientId);

    /// Removes every trusted client and every revocation tombstone, securely clearing every
    /// removed credential.
    /// @return Whether the underlying `Save` succeeded. On failure the in-memory state is left
    ///     unchanged.
    [[nodiscard]] bool Reset();

private:
    /// Constructs a store from already-loaded state.
    TrustStore(ITrustStorePersistence& persistence, ShortIdGenerator shortIdGenerator,
               TrustStoreSnapshot snapshot, bool wasCorruptOnLoad);

    /// Generates a `shortId` not currently used by any trusted record, retrying on collision up
    /// to a bounded attempt count.
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

    /// Currently trusted clients, keyed by `clientId`.
    std::unordered_map<std::string, TrustedClientRecord> records_;

    /// Revoked client identifiers with no current trust.
    std::unordered_set<std::string> tombstones_;

    /// Whether the most recent load fell back to empty due to corrupt/inaccessible persistence.
    bool wasCorruptOnLoad_;
};

}  // namespace dovahlink::security

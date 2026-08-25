#pragma once

#include "security/i_trust_store_persistence.hpp"
#include "security/known_device_record.hpp"
#include "security/trust_mutation_generation.hpp"
#include "security/trust_store_snapshot.hpp"

#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace dovahlink::security {

///  Trust-store operations needed by per-device administration.
class ITrustDeviceStore {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustDeviceStore() = default;

    ///  Returns every currently trusted device.
    [[nodiscard]] virtual std::vector<KnownDeviceRecord> ListTrusted() = 0;

    ///  Returns every known device regardless of state.
    [[nodiscard]] virtual std::vector<KnownDeviceRecord> ListAll() = 0;

    ///  Finds a known device by its administration-only short identifier.
    [[nodiscard]] virtual std::optional<KnownDeviceRecord>
    FindByShortId(std::string_view shortId) = 0;

    ///  Revokes a currently trusted client.
    [[nodiscard]] virtual bool Revoke(const std::string& clientId) = 0;

    ///  Blocks a known client.
    [[nodiscard]] virtual BlockOutcome Block(const std::string& clientId) = 0;

    ///  Unblocks a known client.
    [[nodiscard]] virtual UnblockOutcome
    Unblock(const std::string& clientId) = 0;

    ///  Forgets an eligible known client.
    [[nodiscard]] virtual ForgetOutcome
    Forget(const std::string& clientId) = 0;
};

///  Trust-store operations needed by bulk trust administration.
class ITrustResetStore {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustResetStore() = default;

    ///  Returns every currently trusted device.
    [[nodiscard]] virtual std::vector<KnownDeviceRecord> ListTrusted() = 0;

    ///  Permanently removes every known device.
    [[nodiscard]] virtual bool Reset() = 0;

    ///  Revokes every trusted device while preserving its identity record.
    [[nodiscard]] virtual bool ResetTrust() = 0;
};

///  Thread-safe persistent-trust domain service: load, persist, revoke, reset,
///  query. Fails closed on corrupt or inaccessible persistence -- it never
///  crashes, never silently trusts a client, and always supports a clean
///  reset-and-re-pair path. `KnownDeviceRecord::displayName`'s bound and
///  character invariants are enforced only by `Persist`, the sole production
///  construction path; records obtained any other way are trusted as already
///  valid.
class TrustStore : public ITrustDeviceStore, public ITrustResetStore {
  public:
    ///  Produces one five-digit `shortId` candidate, or `std::nullopt` when the
    ///  underlying random source fails. `Persist` retries this on collision with a
    ///  currently trusted `shortId`.
    using ShortIdGenerator = std::function<std::optional<std::string>()>;

    ///  Loads a `TrustStore` from `persistence`, falling back to an empty,
    ///  non-corrupt store when `persistence.Load()` reports corruption or
    ///  inaccessibility; `WasCorruptOnLoad()` reports which happened.
    ///  @param persistence Storage backend that outlives this store.
    ///  @param shortIdGenerator Produces each five-digit `shortId` candidate
    ///  `Persist` tries;
    ///      defaults to the real CSPRNG-backed generator. Tests inject a
    ///      deterministic or collision-forcing generator here.
    [[nodiscard]] static TrustStore
    Load(ITrustStorePersistence& persistence,
         ShortIdGenerator shortIdGenerator = DefaultShortIdGenerator);

    ///  Generates one five-digit `shortId` candidate using the system CSPRNG.
    [[nodiscard]] static std::optional<std::string> DefaultShortIdGenerator();

    ///  Reports whether the most recent load found persistence corrupt or
    ///  inaccessible and fell back to an empty store.
    [[nodiscard]] bool WasCorruptOnLoad() const noexcept;

    ///  Returns the current global and client-scoped generations for `clientId`.
    ///  @param clientId Client identity whose block generation is included.
    ///  Pairing code captures this fence when it becomes pending and supplies it
    ///  to `PersistIfGeneration` before trust is saved.
    [[nodiscard]] TrustMutationGeneration
    CurrentMutationGeneration(const std::string& clientId);

    ///  Returns the known device record for `clientId`, if it is currently in the
    ///  `kTrusted` state.
    [[nodiscard]] std::optional<KnownDeviceRecord>
    Query(const std::string& clientId);

    ///  Returns every currently trusted (`kTrusted`) device, for administration
    ///  listings.
    [[nodiscard]] std::vector<KnownDeviceRecord> ListTrusted() override;

    ///  Returns every known device, regardless of its current durable state, for
    ///  administration listings.
    [[nodiscard]] std::vector<KnownDeviceRecord> ListAll() override;

    ///  Reports whether `clientId` is a known device currently in the `kRevoked`
    ///  state.
    [[nodiscard]] bool IsRevoked(const std::string& clientId);

    ///  Reports whether `clientId` is a known device currently in the `kBlocked`
    ///  state.
    [[nodiscard]] bool IsBlocked(const std::string& clientId);

    ///  Returns the known device record for `shortId`, regardless of its current
    ///  state. Unlike `Query`/`ListTrusted`, this is not restricted to `kTrusted`
    ///  devices -- administration targets a device by `shortId` without knowing
    ///  its current state in advance.
    [[nodiscard]] std::optional<KnownDeviceRecord>
    FindByShortId(std::string_view shortId) override;

    ///  Reports whether `presentedCredential` matches the credential currently
    ///  trusted for `clientId`, using a constant-time comparison. `false` for an
    ///  unknown clientId, a clientId not currently `kTrusted`, or an empty
    ///  `presentedCredential`.
    [[nodiscard]] bool
    Authenticate(const std::string& clientId,
                 const std::vector<std::uint8_t>& presentedCredential);

    ///  Binds `credential` to `clientId` and transitions it to `kTrusted`. When
    ///  `clientId` already has a known device record (in any prior state except
    ///  `kBlocked`), reuses its existing `shortId` and `createdAt` -- re-pairing
    ///  never changes a device's identity or mints a second `shortId` for it.
    ///  Otherwise assigns a newly generated unique `shortId` and a fresh
    ///  `createdAt`. `displayName` of `std::nullopt` means the field was omitted:
    ///  for a genuinely new `clientId` this leaves no display name; for a
    ///  re-pairing `clientId` this preserves its existing `displayName` unchanged.
    ///  A supplied `displayName` (including an explicit empty string, which clears
    ///  the name) always replaces whatever the record previously held. Rejects an
    ///  empty `clientId` or `credential`, rejects a `displayName` that exceeds the
    ///  configured length bound or contains a control character, and rejects a
    ///  currently `kBlocked` `clientId` outright -- it must be transitioned to
    ///  `kUnpaired` via `Unblock` before it can re-pair.
    ///  @return The new record, or `std::nullopt` when validation, `shortId`
    ///  generation, or the
    ///      underlying `Save` fails. On any failure the in-memory state is left
    ///      unchanged.
    [[nodiscard]] std::optional<KnownDeviceRecord>
    Persist(std::string clientId, std::vector<std::uint8_t> credential,
            std::optional<std::string> displayName);

    ///  Persists a pairing result only when neither a global nor a client-scoped
    ///  administrative trust mutation has occurred since `expectedGeneration`
    ///  was captured. The fence check and persistence occur under the same
    ///  trust-store lock.
    ///  @return The trusted record, or `std::nullopt` when the generation is
    ///  stale, validation fails, or persistence fails.
    [[nodiscard]] std::optional<KnownDeviceRecord>
    PersistIfGeneration(TrustMutationGeneration expectedGeneration,
                        std::string clientId,
                        std::vector<std::uint8_t> credential,
                        std::optional<std::string> displayName);

    ///  Transitions a currently `kTrusted` `clientId` to `kRevoked`, securely
    ///  clearing its credential while keeping its record (identity, `shortId`,
    ///  `displayName`, `createdAt`). A `clientId` that is unknown or already not
    ///  `kTrusted` is a no-op.
    ///  @return Whether the underlying `Save` succeeded. On failure the in-memory
    ///  state is left
    ///      unchanged.
    [[nodiscard]] bool Revoke(const std::string& clientId) override;

    ///  Transitions a currently `kTrusted` or `kRevoked` `clientId` to `kBlocked`,
    ///  securely clearing its credential and recording `blockedAt`. Blocking
    ///  targets an existing known device record, not a bare identity string: an
    ///  unknown `clientId` reports `kNotFound` rather than being blocked. Does not
    ///  disconnect active sessions or cancel owned pairing challenges; callers
    ///  that need those effects perform them separately (mirroring `Revoke`'s own
    ///  division of responsibility with `ActiveSessionDisconnector`).
    ///  @return `kBlocked` on success; `kAlreadyBlocked`, `kNotEligible`,
    ///  `kNotFound`, or
    ///      `kSaveFailed` otherwise. On any non-`kBlocked` outcome the in-memory
    ///      state is unchanged.
    [[nodiscard]] BlockOutcome Block(const std::string& clientId) override;

    ///  Transitions a currently `kBlocked` `clientId` to `kUnpaired`, clearing
    ///  `blockedAt` and requiring a completely fresh pairing flow to become
    ///  `kTrusted` again. Does not restore any previous credential.
    ///  @return `kUnblocked` on success; `kNotBlocked`, `kNotFound`, or
    ///  `kSaveFailed` otherwise. On
    ///      any non-`kUnblocked` outcome the in-memory state is unchanged.
    [[nodiscard]] UnblockOutcome Unblock(const std::string& clientId) override;

    ///  Removes every known device record, securely clearing every removed
    ///  credential. This is Factory Reset's destructive wipe; `TrustResetService`
    ///  gates it behind a confirmation challenge before calling it. See
    ///  `ResetTrust` for the recoverable, identity-preserving alternative.
    ///  @return Whether the underlying `Save` succeeded. On failure the in-memory
    ///  state is left
    ///      unchanged.
    [[nodiscard]] bool Reset() override;

    ///  Deletes a currently `kRevoked` or `kUnpaired` `clientId`'s known device
    ///  record entirely -- identity, `shortId`, `displayName`, `createdAt`, and
    ///  revocation history -- freeing its `shortId` for future allocation.
    ///  Forgetting a `kTrusted` device requires revoking it first; forgetting a
    ///  `kBlocked` device requires unblocking it first, since forgetting never
    ///  implicitly lifts a block.
    ///  @return `kForgotten` on success; `kNotEligible`, `kNotFound`, or
    ///  `kSaveFailed` otherwise. On
    ///      any non-`kForgotten` outcome the in-memory state is unchanged.
    [[nodiscard]] ForgetOutcome Forget(const std::string& clientId) override;

    ///  Renames a currently `kTrusted` `clientId`'s `displayName`. An empty
    ///  `displayName` clears the name (stored as `std::nullopt`, never an empty
    ///  string -- "no display name" stays one canonical representation); a
    ///  non-empty `displayName` replaces it after the same length and
    ///  control-character validation `Persist` applies.
    ///  @return `kRenamed` on success; `kInvalidDisplayName`, `kNotEligible`,
    ///  `kNotFound`, or
    ///      `kSaveFailed` otherwise. On any non-`kRenamed` outcome the in-memory
    ///      state is unchanged.
    [[nodiscard]] RenameOutcome Rename(const std::string& clientId,
                                       std::string displayName);

    ///  Transitions every currently `kTrusted` device to `kRevoked`, securely
    ///  clearing each credential, while leaving every `kRevoked`, `kBlocked`, and
    ///  `kUnpaired` device -- and every device's identity fields (`clientId`,
    ///  `shortId`, `displayName`, `createdAt`, `blockedAt`) -- completely
    ///  untouched. This is Reset Trust: recoverable and non-destructive to Known
    ///  Device records, unlike `Reset`.
    ///  @return Whether the underlying `Save` succeeded. On failure the in-memory
    ///  state is left
    ///      unchanged.
    [[nodiscard]] bool ResetTrust() override;

  private:
    ///  Constructs a store from already-loaded state.
    TrustStore(ITrustStorePersistence& persistence,
               ShortIdGenerator shortIdGenerator, TrustStoreSnapshot snapshot,
               bool wasCorruptOnLoad);

    ///  Generates a `shortId` not currently used by any known device record in any
    ///  state, retrying on collision up to a bounded attempt count -- a `shortId`
    ///  stays reserved for as long as its record exists, regardless of that
    ///  record's current state.
    ///  @return The generated `shortId`, or `std::nullopt` on generator failure or
    ///  attempt
    ///      exhaustion.
    [[nodiscard]] std::optional<std::string> GenerateUniqueShortId();

    ///  Persists a trusted record while the caller already holds `mutex_`.
    [[nodiscard]] std::optional<KnownDeviceRecord>
    PersistLocked(std::string clientId, std::vector<std::uint8_t> credential,
                  std::optional<std::string> displayName);

    ///  Reports whether `displayName` satisfies the length bound and is free of
    ///  control characters.
    [[nodiscard]] static bool IsValidDisplayName(const std::string& displayName);

    ///  Returns the current mutation fence while the caller already holds
    ///  `mutex_`.
    [[nodiscard]] TrustMutationGeneration
    CurrentMutationGenerationLocked(const std::string& clientId) const;

    ///  Builds the current in-memory state into a snapshot suitable for `Save`.
    [[nodiscard]] TrustStoreSnapshot BuildSnapshot() const;

    ///  Storage backend this store reads from and writes to.
    ITrustStorePersistence* persistence_;

    ///  Produces each five-digit `shortId` candidate.
    ShortIdGenerator shortIdGenerator_;

    ///  Serializes access to in-memory trust state.
    std::mutex mutex_;

    ///  Advances after each successful Reset Trust or Factory Reset.
    std::uint64_t globalMutationGeneration_ = 0;

    ///  Advances for each client after a successful Revoke or Block of that
    ///  client.
    std::unordered_map<std::string, std::uint64_t>
        clientMutationGenerations_;

    ///  Every known device, keyed by `clientId`, regardless of state.
    std::unordered_map<std::string, KnownDeviceRecord> devices_;

    ///  Whether the most recent load fell back to empty due to
    ///  corrupt/inaccessible persistence.
    bool wasCorruptOnLoad_;
};

} //  namespace dovahlink::security

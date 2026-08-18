#pragma once

#include "security/confirm_code_result.hpp"
#include "security/enums.hpp"
#include "security/renotify_result.hpp"
#include "security/start_challenge_result.hpp"
#include "security/token_store.hpp"

#include <chrono>
#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <vector>

namespace dovahlink::security {

/// The credential-issuance data a successful `PairingSession::TryFinalize` returns, ready for the
/// caller to commit via `TrustStore::Persist`.
struct PendingCredential {
    /// The pairing client's stable protocol identity.
    std::string clientId;
    /// The strong credential the Bridge generated for this pairing attempt.
    std::vector<std::uint8_t> credential;
    /// The optional presentation-only label the client supplied with its code.
    std::optional<std::string> displayName;
    /// When this credential entered `PENDING_CREDENTIAL`, for `kPairingPendingCredentialTtl`'s
    /// lazy-expiry check in `PeekPending`/`CommitPending`/`TryStartChallenge`/`TryCancel`.
    std::chrono::steady_clock::time_point pendingSince;
};

/// In-memory `NONE -> CHALLENGE_ACTIVE -> PENDING_CREDENTIAL -> NONE` pairing lifecycle, per
/// `ai/context/protocol/security.md`'s "Persistent local trust" state machine and wire-message
/// mapping. Knows nothing about wire messages, `TrustStore`, or Skyrim; a caller (the pairing
/// handler) supplies the generated credential and commits a successful `TryFinalize`'s result to
/// `TrustStore` itself. Never persists across a Bridge restart -- "Incomplete pending pairing does
/// not need to survive a bridge restart."
class PairingSession {
public:
    /// Produces one six-digit pairing-code candidate, or `std::nullopt` when the underlying random
    /// source fails.
    using CodeGenerator = std::function<std::optional<std::string>()>;

    /// Creates a session with no active challenge.
    /// @param codeGenerator Produces each six-digit code `TryStartChallenge` issues; defaults to
    ///     the real CSPRNG-backed generator. Tests inject a deterministic generator here.
    /// @param codeTimeToLive How long a started challenge's code remains valid. Tests use a short
    ///     or zero duration to exercise expiry deterministically, matching `TokenStore`'s own
    ///     constructor.
    explicit PairingSession(CodeGenerator codeGenerator = DefaultCodeGenerator,
                             std::chrono::steady_clock::duration codeTimeToLive = std::chrono::minutes(5));

    /// Generates one six-digit pairing code using the system CSPRNG.
    [[nodiscard]] static std::optional<std::string> DefaultCodeGenerator();

    /// Starts a new challenge (`NONE -> CHALLENGE_ACTIVE`) if none is active and no credential is
    /// currently pending finalization, or reports the existing challenge/pending credential's
    /// ownership per `ai/context/protocol/security.md`'s Phase 3.1 ownership model. Applies the
    /// reconnect-grace and pending-credential lazy-expiry checks before deciding.
    /// @param clientId The requesting client's identity.
    /// @param now Current monotonic time, for the lazy-expiry checks.
    /// @return `kStarted` with the six-digit code to display; `kResumed` when `clientId` already
    ///     owns the active challenge or pending credential; `kOtherDeviceActive` when a different
    ///     `clientId` owns it; or `kGeneratorFailed` when the underlying random source fails.
    [[nodiscard]] StartChallengeResult TryStartChallenge(const std::string& clientId,
                                                          std::chrono::steady_clock::time_point now);

    /// Returns the active challenge's remaining code validity, applying the same reconnect-grace
    /// lazy-expiry check as `TryStartChallenge`.
    /// @param now Current monotonic time, for the reconnect-grace lazy-expiry check.
    /// @return The remaining duration, or `std::nullopt` when no challenge is active (including a
    ///     `PENDING_CREDENTIAL` state, which has no code left to redisplay).
    [[nodiscard]] std::optional<std::chrono::seconds> RemainingSeconds(
        std::chrono::steady_clock::time_point now);

    /// Records that `clientId`'s connection has just disconnected, starting the reconnect-grace
    /// countdown, if and only if `clientId` currently owns an active challenge (`CHALLENGE_ACTIVE`).
    /// A no-op for a non-owner, or once the challenge has reached `PENDING_CREDENTIAL` -- the grace
    /// period does not apply there; a pending credential's own 5-minute expiry governs instead.
    /// @param clientId The disconnecting client's identity.
    /// @param now Current monotonic time, stamped as the disconnect moment.
    void NotifyDisconnected(const std::string& clientId, std::chrono::steady_clock::time_point now);

    /// Clears a pending reconnect-grace countdown for `clientId`, if it owns the active challenge
    /// and was mid-countdown. Applies the lazy-expiry check first, so a reconnect arriving after
    /// the grace period already elapsed correctly finds no ownership left to resume.
    /// @param clientId The reconnecting client's identity.
    /// @param now Current monotonic time, for the reconnect-grace lazy-expiry check.
    void NotifyReconnected(const std::string& clientId, std::chrono::steady_clock::time_point now);

    /// Validates `presentedCode` against the active challenge (constant-time, single-use).
    /// Applies the reconnect-grace lazy-expiry check first, then rejects as `kInvalid` --
    /// indistinguishable from a wrong code -- when a different `clientId` owns the active
    /// challenge, so a non-owner cannot blind-guess a code that is not theirs. A pacing check
    /// (`kPairingConfirmPacingInterval`) rejects an attempt evaluated too soon after the previous
    /// one as `kPacingLimited` without counting it as wrong; a wrong evaluated attempt counts
    /// toward `kPairingMaxWrongAttempts`, and the attempt that reaches it cancels the challenge
    /// outright (`kHardLimitReached`) instead of leaving it guessable further. On a match,
    /// transitions `CHALLENGE_ACTIVE -> PENDING_CREDENTIAL`, holding `clientId`, `credential`, and
    /// `displayName` for a later `TryFinalize`.
    /// @param presentedCode The code the client submitted.
    /// @param now Current monotonic time, used for the lazy-expiry checks and pacing/attempt
    ///     accounting.
    /// @param clientId The pairing client's identity, checked against the challenge owner and
    ///     held pending.
    /// @param credential The credential the caller generated for this attempt, to hold pending.
    /// @param displayName The client-supplied optional label, to hold pending.
    [[nodiscard]] ConfirmCodeResult TryConfirmCode(const std::string& presentedCode,
                                                    std::chrono::steady_clock::time_point now,
                                                    std::string clientId, std::vector<std::uint8_t> credential,
                                                    std::optional<std::string> displayName);

    /// Matches `clientId` and `credential` against the pending credential without consuming it, so
    /// a caller can attempt `TrustStore::Persist` before committing. A mismatch leaves the pending
    /// credential untouched, allowing a correct retry. Applies the pending-credential lazy-expiry
    /// check first.
    /// @param now Current monotonic time, for the pending-credential lazy-expiry check.
    /// @return A copy of the pending credential, or `std::nullopt` when it does not match, is
    ///     expired, or none is pending (including after a Bridge restart lost it).
    [[nodiscard]] std::optional<PendingCredential> PeekPending(const std::string& clientId,
                                                                const std::vector<std::uint8_t>& credential,
                                                                std::chrono::steady_clock::time_point now);

    /// Re-matches `clientId` and `credential` against the pending credential and, on a match,
    /// transitions `PENDING_CREDENTIAL -> NONE`. Call only after `PeekPending` and a successful
    /// `TrustStore::Persist`: a caller that skips committing on a failed persist leaves the pending
    /// credential in place for a retry instead of losing it. Applies the pending-credential
    /// lazy-expiry check first.
    /// @param now Current monotonic time, for the pending-credential lazy-expiry check.
    /// @return `true` if the pending credential matched and was cleared; `false` if it did not
    ///     match, was expired, or none is pending.
    [[nodiscard]] bool CommitPending(const std::string& clientId, const std::vector<std::uint8_t>& credential,
                                      std::chrono::steady_clock::time_point now);

    /// "Show code again": if `clientId` owns the active challenge and its own
    /// `kPairingRenotifyCooldown` has elapsed, starts a fresh cooldown and reports `kRenotified` --
    /// the caller redisplays the existing code via the notification sink, never a new one. Never
    /// reveals or returns the code itself; the caller already has it from the original display.
    /// Independent of the auto-renotify cooldown `TryConfirmCode` manages for wrong-code
    /// redisplays -- neither affects the other.
    /// @param clientId The requesting client's identity.
    /// @param now Current monotonic time, for the reconnect-grace lazy-expiry check and cooldown
    ///     accounting.
    /// @return `kRenotified` when the caller should redisplay the code now; `kCooldown` with the
    ///     remaining wait when it owns the challenge but must wait; `kNotActive` when it owns no
    ///     active challenge or pending credential.
    [[nodiscard]] RenotifyResult TryRenotify(const std::string& clientId,
                                              std::chrono::steady_clock::time_point now);

    /// Cancels `clientId`'s owned active challenge or pending credential, whichever is currently
    /// held. Idempotent without pretending work occurred: reports `kAlreadyIdle`, not
    /// `kCancelled`, when `clientId` owns nothing. Never touches `TrustStore` or any already-
    /// committed trust; only ever clears in-memory challenge/pending state.
    /// @param clientId The requesting client's identity.
    /// @param now Current monotonic time, for the lazy-expiry checks.
    [[nodiscard]] CancelOutcome TryCancel(const std::string& clientId, std::chrono::steady_clock::time_point now);

private:
    /// Clears `activeChallenge_` and `ownerClientId_` once `disconnectedAt_` is set and
    /// `now - *disconnectedAt_ >= kPairingReconnectGracePeriod`, per
    /// `ai/context/protocol/security.md`'s reconnect-grace lazy-expiry model -- evaluated on next
    /// access rather than by an active timer. A no-op when no disconnect is pending. Call while
    /// holding `mutex_`.
    /// @param now Current monotonic time.
    void ExpireOwnerIfGraceElapsedLocked(std::chrono::steady_clock::time_point now);

    /// Clears `pendingCredential_` once `now - pendingCredential_->pendingSince >=
    /// kPairingPendingCredentialTtl`, evaluated on next access rather than by an active timer. A
    /// no-op when no credential is pending. Call while holding `mutex_`.
    /// @param now Current monotonic time.
    void ExpirePendingIfElapsedLocked(std::chrono::steady_clock::time_point now);

    /// Resets every field scoped to one active challenge's lifetime (`activeChallenge_`,
    /// `ownerClientId_`, `disconnectedAt_`, the wrong-attempt/pacing counters, and both renotify
    /// cooldowns) back to their `NONE` state together, so no field can accidentally carry stale
    /// data into the next challenge. Call while holding `mutex_`. Never touches
    /// `pendingCredential_`, which has its own independent lifetime.
    void ClearChallengeLocked();

    /// Serializes access to challenge and pending-credential state.
    std::mutex mutex_;

    /// Produces each six-digit code candidate.
    CodeGenerator codeGenerator_;

    /// How long a newly started challenge's code remains valid.
    std::chrono::steady_clock::duration codeTimeToLive_;

    /// The active challenge's single-use, expiring code, or no value when `NONE`.
    std::optional<TokenStore> activeChallenge_;

    /// The `clientId` that owns `activeChallenge_`, or no value when it does not exist (`NONE` or
    /// `PENDING_CREDENTIAL` -- cleared together with `activeChallenge_` on every exit from
    /// `CHALLENGE_ACTIVE`). Pending-credential ownership is tracked separately by
    /// `PendingCredential::clientId`.
    std::optional<std::string> ownerClientId_;

    /// When `ownerClientId_`'s connection disconnected while owning `activeChallenge_`, or no
    /// value while it is connected or no challenge is owned. Cleared by `NotifyReconnected` or by
    /// `ExpireOwnerIfGraceElapsedLocked` once the grace period elapses. Never set while the
    /// challenge is `PENDING_CREDENTIAL` -- the grace period does not apply there.
    std::optional<std::chrono::steady_clock::time_point> disconnectedAt_;

    /// Wrong evaluated `TryConfirmCode` attempts against `activeChallenge_` so far, reset whenever
    /// a fresh challenge starts. The attempt that reaches `kPairingMaxWrongAttempts` cancels the
    /// challenge via `ClearChallengeLocked`, so this can never itself hold a stale count from a
    /// prior challenge.
    std::size_t wrongAttempts_ = 0;

    /// When `TryConfirmCode` last actually evaluated a code against `activeChallenge_` (passed the
    /// ownership and expiry checks and reached `TokenStore::TryReserve`), or no value before the
    /// first evaluated attempt. Backs the `kPairingConfirmPacingInterval` pacing check -- distinct
    /// from `wrongAttempts_`, since a paced-out attempt is never evaluated and never counts as
    /// wrong.
    std::optional<std::chrono::steady_clock::time_point> lastConfirmAttemptAt_;

    /// When `TryRenotify`'s manual "show code again" cooldown next allows a fresh redisplay, or no
    /// value before the first renotify. Independent of `autoRenotifyCooldownUntil_`.
    std::optional<std::chrono::steady_clock::time_point> renotifyCooldownUntil_;

    /// When `TryConfirmCode`'s automatic wrong-code redisplay cooldown next allows another
    /// automatic redisplay, or no value before the first one. Independent of
    /// `renotifyCooldownUntil_`; reuses the same `kPairingRenotifyCooldown` duration as a
    /// deliberate choice (both exist to prevent Skyrim notification spam for the same code), but
    /// is tracked separately so neither path's cooldown affects the other's.
    std::optional<std::chrono::steady_clock::time_point> autoRenotifyCooldownUntil_;

    /// The credential-issuance data awaiting `TryFinalize`, or no value when not `PENDING_CREDENTIAL`.
    std::optional<PendingCredential> pendingCredential_;
};

}  // namespace dovahlink::security

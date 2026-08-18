#pragma once

#include "security/enums.hpp"
#include "security/throttle.hpp"
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

    /// Result of a `TryStartChallenge` call.
    struct StartChallengeResult {
        /// Which of the four outcomes occurred.
        StartChallengeOutcome outcome;
        /// The six-digit code to display, populated only when `outcome ==
        /// StartChallengeOutcome::kStarted`.
        std::optional<std::string> code;
    };

    /// Starts a new challenge (`NONE -> CHALLENGE_ACTIVE`) if none is active and no credential is
    /// currently pending finalization, or reports the existing challenge/pending credential's
    /// ownership per `ai/context/protocol/security.md`'s Phase 3.1 ownership model. Applies the
    /// reconnect-grace lazy-expiry check (see `ExpireOwnerIfGraceElapsedLocked`) before deciding.
    /// @param clientId The requesting client's identity.
    /// @param now Current monotonic time, for the reconnect-grace lazy-expiry check.
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

    /// Validates `presentedCode` against the active challenge (constant-time, single-use,
    /// attempt-limited against a throttle owned by and scoped to this session). Applies the
    /// reconnect-grace lazy-expiry check first, then rejects as `kInvalid` -- indistinguishable
    /// from a wrong code -- when a different `clientId` owns the active challenge, so a non-owner
    /// cannot blind-guess a code that is not theirs. On a match, transitions
    /// `CHALLENGE_ACTIVE -> PENDING_CREDENTIAL`, holding `clientId`, `credential`, and
    /// `displayName` for a later `TryFinalize`.
    /// @param presentedCode The code the client submitted.
    /// @param now Current monotonic time, used for the reconnect-grace lazy-expiry check and
    ///     attempt-limit accounting.
    /// @param clientId The pairing client's identity, checked against the challenge owner and
    ///     held pending.
    /// @param credential The credential the caller generated for this attempt, to hold pending.
    /// @param displayName The client-supplied optional label, to hold pending.
    [[nodiscard]] ConfirmResult TryConfirmCode(const std::string& presentedCode,
                                                std::chrono::steady_clock::time_point now, std::string clientId,
                                                std::vector<std::uint8_t> credential,
                                                std::optional<std::string> displayName);

    /// Matches `clientId` and `credential` against the pending credential without consuming it, so
    /// a caller can attempt `TrustStore::Persist` before committing. A mismatch leaves the pending
    /// credential untouched, allowing a correct retry.
    /// @return A copy of the pending credential, or `std::nullopt` when it does not match or none
    ///     is pending (including after a Bridge restart lost it).
    [[nodiscard]] std::optional<PendingCredential> PeekPending(const std::string& clientId,
                                                                const std::vector<std::uint8_t>& credential);

    /// Re-matches `clientId` and `credential` against the pending credential and, on a match,
    /// transitions `PENDING_CREDENTIAL -> NONE`. Call only after `PeekPending` and a successful
    /// `TrustStore::Persist`: a caller that skips committing on a failed persist leaves the pending
    /// credential in place for a retry instead of losing it.
    /// @return `true` if the pending credential matched and was cleared; `false` if it did not
    ///     match or none is pending.
    [[nodiscard]] bool CommitPending(const std::string& clientId, const std::vector<std::uint8_t>& credential);

private:
    /// Clears `activeChallenge_` and `ownerClientId_` once `disconnectedAt_` is set and
    /// `now - *disconnectedAt_ >= kPairingReconnectGracePeriod`, per
    /// `ai/context/protocol/security.md`'s reconnect-grace lazy-expiry model -- evaluated on next
    /// access rather than by an active timer. A no-op when no disconnect is pending. Call while
    /// holding `mutex_`.
    /// @param now Current monotonic time.
    void ExpireOwnerIfGraceElapsedLocked(std::chrono::steady_clock::time_point now);

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

    /// Attempt-limits wrong `TryConfirmCode` guesses, scoped to this session only -- distinct from
    /// `hello`'s own token throttle, so guessing a pairing code cannot block or be blocked by an
    /// unrelated developer-token attempt.
    FailedTokenThrottle codeAttemptThrottle_;

    /// The credential-issuance data awaiting `TryFinalize`, or no value when not `PENDING_CREDENTIAL`.
    std::optional<PendingCredential> pendingCredential_;
};

}  // namespace dovahlink::security

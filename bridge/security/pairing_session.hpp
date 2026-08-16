#pragma once

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

    /// Starts a new challenge (`NONE -> CHALLENGE_ACTIVE`) if none is active and no credential is
    /// currently pending finalization.
    /// @return The six-digit code to display, or `std::nullopt` when a challenge or a pending
    ///     credential is already active ("already in progress" -- the caller must not generate or
    ///     display a second code) or the code generator itself fails.
    [[nodiscard]] std::optional<std::string> TryStartChallenge();

    /// Validates `presentedCode` against the active challenge (constant-time, single-use,
    /// expiry-checked, attempt-limited against a throttle owned by and scoped to this session).
    /// On a match, transitions `CHALLENGE_ACTIVE -> PENDING_CREDENTIAL`, holding `clientId`,
    /// `credential`, and `displayName` for a later `TryFinalize`.
    /// @param presentedCode The code the client submitted.
    /// @param now Current monotonic time, used only for attempt-limit accounting.
    /// @param clientId The pairing client's identity, to hold pending.
    /// @param credential The credential the caller generated for this attempt, to hold pending.
    /// @param displayName The client-supplied optional label, to hold pending.
    /// @return Whether the code was valid. `false` for an invalid, expired, already-consumed, or
    ///     throttled code, or when no challenge is active.
    [[nodiscard]] bool TryConfirmCode(const std::string& presentedCode, std::chrono::steady_clock::time_point now,
                                       std::string clientId, std::vector<std::uint8_t> credential,
                                       std::optional<std::string> displayName);

    /// Matches `clientId` and `credential` against the pending credential. On a match, transitions
    /// `PENDING_CREDENTIAL -> NONE` and returns it, ready for `TrustStore::Persist`. A mismatch
    /// leaves the pending credential untouched, allowing a correct retry.
    /// @return The pending credential, or `std::nullopt` when it does not match or none is
    ///     pending (including after a Bridge restart lost it).
    [[nodiscard]] std::optional<PendingCredential> TryFinalize(const std::string& clientId,
                                                                const std::vector<std::uint8_t>& credential);

private:
    /// Serializes access to challenge and pending-credential state.
    std::mutex mutex_;

    /// Produces each six-digit code candidate.
    CodeGenerator codeGenerator_;

    /// How long a newly started challenge's code remains valid.
    std::chrono::steady_clock::duration codeTimeToLive_;

    /// The active challenge's single-use, expiring code, or no value when `NONE`.
    std::optional<TokenStore> activeChallenge_;

    /// Attempt-limits wrong `TryConfirmCode` guesses, scoped to this session only -- distinct from
    /// `hello`'s own token throttle, so guessing a pairing code cannot block or be blocked by an
    /// unrelated developer-token attempt.
    FailedTokenThrottle codeAttemptThrottle_;

    /// The credential-issuance data awaiting `TryFinalize`, or no value when not `PENDING_CREDENTIAL`.
    std::optional<PendingCredential> pendingCredential_;
};

}  // namespace dovahlink::security

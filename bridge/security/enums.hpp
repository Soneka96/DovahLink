#pragma once

namespace dovahlink::security {

// ---- Developer token ----

/// Outcome of reading the developer-authentication token from the environment.
enum class TokenReadOutcome {
    kMissing,    ///< The variable is unset or empty.
    kMalformed,  ///< The variable is set but is not valid 64-character hex token data.
    kValid,      ///< The variable decoded to a well-formed 32-byte token.
};

// ---- Pairing ----

/// Outcome of `PairingSession::TryStartChallenge`. Kept distinct from a plain `optional<string>`
/// because each case needs different caller behavior: `kStarted` has a fresh code to display,
/// `kResumed`/`kOtherDeviceActive` must not generate or display a second one, and
/// `kGeneratorFailed` must report pairing as unavailable rather than leaving the caller waiting on
/// a code that will never arrive.
enum class StartChallengeOutcome {
    /// A new challenge was started; `PairingSession::StartChallengeResult::code` holds the
    /// six-digit code to display.
    kStarted,
    /// The calling `clientId` already owns the active challenge or pending credential; no new code
    /// is generated or displayed. Use `PairingSession::RemainingSeconds` for the active
    /// challenge's remaining validity.
    kResumed,
    /// A different `clientId` owns the active challenge or pending credential; the caller must not
    /// generate, display, or reveal anything about the existing code or its owner.
    kOtherDeviceActive,
    /// The code generator itself failed.
    kGeneratorFailed,
};

/// Outcome of `PairingSession::TryConfirmCode`. `kExpired` and `kInvalid` are reported separately
/// (unlike `TokenReadOutcome`'s deliberately undifferentiated hello-token failures) because
/// knowing which one happened is genuine UX information for a human re-entering a code ("get a new
/// code" vs. "check what you typed"), not a security-relevant distinction: neither case reveals
/// anything that helps guess the code faster.
enum class ConfirmResult {
    /// The code matched; a credential is now pending finalization.
    kConfirmed,
    /// The session's `codeAttemptThrottle_` currently blocks attempts.
    kRateLimited,
    /// A challenge existed but is no longer available (its code expired, or was already consumed
    /// by an earlier successful attempt).
    kExpired,
    /// No challenge was ever active, the presented code did not match the active one, or the
    /// presenting `clientId` does not own the active challenge.
    kInvalid,
};

}  // namespace dovahlink::security

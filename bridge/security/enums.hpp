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
    /// A new challenge was started; `StartChallengeResult::code` holds the six-digit code to
    /// display.
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
/// anything that helps guess the code faster. `kPacingLimited` and `kHardLimitReached` replace the
/// single undifferentiated rate-limit outcome earlier phases used: pacing blocks an attempt
/// without counting against the wrong-attempt budget, while the hard limit is a terminal count of
/// wrong attempts that destroys the challenge.
enum class ConfirmResult {
    /// The code matched; a credential is now pending finalization.
    kConfirmed,
    /// Evaluated less than `kPairingConfirmPacingInterval` after the previous evaluated attempt;
    /// this attempt was not evaluated at all and does not count toward the wrong-attempt limit.
    kPacingLimited,
    /// A challenge existed but is no longer available (its code expired, or was already consumed
    /// by an earlier successful attempt).
    kExpired,
    /// No challenge was ever active, the presented code did not match the active one, or the
    /// presenting `clientId` does not own the active challenge.
    kInvalid,
    /// The wrong-attempt limit (`kPairingMaxWrongAttempts`) was just reached; the challenge is
    /// cancelled and its code destroyed as part of this outcome, and a fresh `pairing_request` is
    /// required to try again.
    kHardLimitReached,
};

/// Outcome of `PairingSession::TryRenotify`.
enum class RenotifyOutcome {
    /// The caller owns an active challenge, and its own renotify cooldown allows a fresh Skyrim
    /// redisplay of the code now.
    kRenotified,
    /// The caller owns an active challenge, but its own renotify cooldown has not elapsed yet.
    kCooldown,
    /// The caller owns no active challenge or pending credential to renotify.
    kNotActive,
};

/// Outcome of `PairingSession::TryCancel`.
enum class CancelOutcome {
    /// The caller's owned active challenge or pending credential was cancelled.
    kCancelled,
    /// The caller owned nothing to cancel.
    kAlreadyIdle,
};

// ---- Known device ----

/// The four durable states one persistent `KnownDeviceRecord` can occupy. A Known Device is always
/// in exactly one of these; only `kTrusted` carries a usable credential.
enum class KnownDeviceState {
    /// Holds a usable credential and can authenticate normally.
    kTrusted,
    /// Trust was removed; the device may re-pair to become `kTrusted` again under the same
    /// identity.
    kRevoked,
    /// Rejected at `hello` and at pairing; requires an explicit unblock before it can do either
    /// again.
    kBlocked,
    /// No longer blocked but not yet re-paired; requires a completely fresh pairing flow to become
    /// `kTrusted` again.
    kUnpaired,
};

/// Outcome of `TrustStore::Block`.
enum class BlockOutcome {
    /// The device transitioned from `kTrusted` or `kRevoked` to `kBlocked`.
    kBlocked,
    /// The device was already `kBlocked`; nothing changed.
    kAlreadyBlocked,
    /// A known device record exists for the identity, but its current state (`kUnpaired`) is not
    /// eligible for blocking in this phase.
    kNotEligible,
    /// No known device record exists for the identity at all.
    kNotFound,
    /// The transition was valid but the underlying persistence `Save` failed; in-memory state was
    /// rolled back.
    kSaveFailed,
};

/// Outcome of `TrustStore::Unblock`.
enum class UnblockOutcome {
    /// The device transitioned from `kBlocked` to `kUnpaired`.
    kUnblocked,
    /// A known device record exists for the identity, but it is not currently `kBlocked`.
    kNotBlocked,
    /// No known device record exists for the identity at all.
    kNotFound,
    /// The transition was valid but the underlying persistence `Save` failed; in-memory state was
    /// rolled back.
    kSaveFailed,
};

}  // namespace dovahlink::security

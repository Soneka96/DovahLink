#pragma once

namespace dovahlink::application {

//  ---- Application ----

///  A session's message-type allowlist, per `ai/context/protocol/security.md`'s
///  "Hello authentication and session trust tiers".
enum class SessionTrustTier {
    ///  Admitted without a trust credential (the `unpaired` hello auth method);
    ///  allowed only the
    ///  pairing-flow messages until pairing succeeds.
    kRestricted,
    ///  Ordinary authenticated access (developer token or a trusted device
    ///  credential), or a
    ///  restricted session upgraded in place by a successful pairing confirmation.
    kFull,
};

///  How a session authenticated at `hello`, independent of `SessionTrustTier`'s
///  "what it may do now." Set once when the session is created and never changed
///  afterward -- a session that started `kUnpaired` and later completes pairing
///  on the same connection has its `SessionTrustTier` upgraded to `kFull` in
///  place, but its `authMethod` stays `kUnpaired`. Distinguishes
///  `kDeveloperToken` sessions, which are never treated as Known Devices
///  (`ai/context/protocol/security.md`'s "Developer authentication"), from the
///  other two.
enum class SessionAuthMethod {
    ///  Admitted via the bootstrap `unpaired` hello auth method; no credential
    ///  presented yet.
    kUnpaired,
    ///  Admitted via a persisted pairing credential (`hello.auth.method`
    ///  `trusted_device_credential`).
    kTrustedDeviceCredential,
    ///  Admitted via the `one_time_local_token` developer-authentication provider.
    kDeveloperToken,
};

///  Lifecycle state of the currently tracked Skyrim play context.
enum class LifecycleState {
    ///  No play context is active: the main menu, or before any game has loaded.
    kNoContext,
    ///  A load or new-game attempt is in progress; no context is active yet.
    kLoading,
    ///  A play context is active and current.
    kActive,
};

///  Raw Skyrim/SKSE lifecycle signals recognized by PlayContextLifecycle.
enum class LifecycleEvent {
    ///  SKSE's serialization revert callback: unconditional teardown of any
    ///  current runtime game state. Fires before every load and new game, not
    ///  only a genuine return to the main menu.
    kRevert,
    ///  SKSE::MessagingInterface::kPreLoadGame: a save has been selected but
    ///  not yet loaded.
    kPreLoadGame,
    ///  SKSE::MessagingInterface::kPostLoadGame carrying a `true` success value.
    kPostLoadGameSuccess,
    ///  SKSE::MessagingInterface::kPostLoadGame carrying a `false` success value.
    kPostLoadGameFailure,
    ///  SKSE::MessagingInterface::kNewGame.
    kNewGame,
};

///  Classifies a message-ID recording attempt.
enum class MessageIdCheckResult {
    ///  The message ID was new and recorded.
    kAccepted,

    ///  The message ID was already recorded for this session.
    kReplayed,
};

//  ---- Capture ----

///  How a captured value is supplied to the bridge, independent of how a
///  subscriber receives it (`UpdateMode`). `CapturePolicy` pairs this with a
///  `RateClass` for `kSampled`.
enum class CapturePolicyKind {
    ///  Captured synchronously from a trustworthy Skyrim/SKSE event; no
    ///  polling cadence applies.
    kNativeEvent,
    ///  Captured on a recurring cadence bounded by a `RateClass`.
    kSampled,
};

///  Maximum capture frequency for a `kSampled` value. A rate class bounds how
///  often the shared cadence scheduler may capture a value; it is not a
///  publication or network-send cadence.
enum class RateClass {
    ///  Captured at most once per `kFastCapturePeriod`.
    kFast,
    ///  Captured at most once per `kMediumCapturePeriod`.
    kMedium,
    ///  Captured at most once per `kSlowCapturePeriod`.
    kSlow,
};

//  ---- Outbound queue ----

///  Bounded outbound queue storage class assigned to a worker-owned
///  publication after encoding, based on its size against the approved
///  threshold. Controls data-lane slot accounting only; it does not change
///  Snapshot/Event reliability and is never a wire field.
enum class QueueClass {
    ///  Occupies one of the data lane's ordinarily sized slots.
    kNormal,
    ///  Occupies one of the data lane's reserved larger-publication slots.
    kHeavy,
};

} //  namespace dovahlink::application

namespace dovahlink::security {

//  ---- Security ----

//  ---- Developer token ----

///  Outcome of reading the developer-authentication token from the environment.
enum class TokenReadOutcome {
    kMissing,   ///<  The variable is unset or empty.
    kMalformed, ///<  The variable is set but is not valid 64-character hex token
                ///<  data.
    kValid,     ///<  The variable decoded to a well-formed 32-byte token.
};

//  ---- Pairing ----

///  Outcome of `PairingSession::TryStartChallenge`. Kept distinct from a plain
///  `optional<string>` because each case needs different caller behavior:
///  `kStarted` has a fresh code to display, `kResumed`/`kOtherDeviceActive` must
///  not generate or display a second one, and `kGeneratorFailed` must report
///  pairing as unavailable rather than leaving the caller waiting on a code that
///  will never arrive.
enum class StartChallengeOutcome {
    ///  A new challenge was started; `StartChallengeResult::code` holds the
    ///  six-digit code to
    ///  display.
    kStarted,
    ///  The calling `clientId` already owns the active challenge or pending
    ///  credential; no new code
    ///  is generated or displayed. Use `PairingSession::RemainingSeconds` for the
    ///  active
    ///  challenge's remaining validity.
    kResumed,
    ///  A different `clientId` owns the active challenge or pending credential;
    ///  the caller must not
    ///  generate, display, or reveal anything about the existing code or its
    ///  owner.
    kOtherDeviceActive,
    ///  The code generator itself failed.
    kGeneratorFailed,
};

///  Outcome of `PairingSession::TryConfirmCode`. `kExpired` and `kInvalid` are
///  reported separately (unlike `TokenReadOutcome`'s deliberately
///  undifferentiated hello-token failures) because knowing which one happened is
///  genuine UX information for a human re-entering a code ("get a new code" vs.
///  "check what you typed"), not a security-relevant distinction: neither case
///  reveals anything that helps guess the code faster. `kPacingLimited` and
///  `kHardLimitReached` replace the single undifferentiated rate-limit outcome
///  earlier phases used: pacing blocks an attempt without counting against the
///  wrong-attempt budget, while the hard limit is a terminal count of wrong
///  attempts that destroys the challenge.
enum class ConfirmResult {
    ///  The code matched; a credential is now pending finalization.
    kConfirmed,
    ///  Evaluated less than `kPairingConfirmPacingInterval` after the previous
    ///  evaluated attempt;
    ///  this attempt was not evaluated at all and does not count toward the
    ///  wrong-attempt limit.
    kPacingLimited,
    ///  A challenge existed but is no longer available (its code expired, or was
    ///  already consumed
    ///  by an earlier successful attempt).
    kExpired,
    ///  No challenge was ever active, the presented code did not match the active
    ///  one, or the
    ///  presenting `clientId` does not own the active challenge.
    kInvalid,
    ///  The wrong-attempt limit (`kPairingMaxWrongAttempts`) was just reached; the
    ///  challenge is
    ///  cancelled and its code destroyed as part of this outcome, and a fresh
    ///  `pairing_request` is
    ///  required to try again.
    kHardLimitReached,
};

///  Outcome of `PairingSession::TryRenotify`.
enum class RenotifyOutcome {
    ///  The caller owns an active challenge, and its own renotify cooldown allows
    ///  a fresh Skyrim
    ///  redisplay of the code now.
    kRenotified,
    ///  The caller owns an active challenge, but its own renotify cooldown has not
    ///  elapsed yet.
    kCooldown,
    ///  The caller owns no active challenge or pending credential to renotify.
    kNotActive,
};

///  Outcome of `PairingSession::TryCancel`.
enum class CancelOutcome {
    ///  The caller's owned active challenge or pending credential was cancelled.
    kCancelled,
    ///  The caller owned nothing to cancel.
    kAlreadyIdle,
};

///  Outcome of the coordinator's pending-pairing commit. Persistence failure is
///  distinct from an administrative invalidation so the caller can preserve a
///  retryable pending credential only for the transient failure case.
enum class PairingCommitOutcome {
    ///  The pending credential was persisted and consumed.
    kCommitted,
    ///  No matching pending credential remained.
    kPendingNotFound,
    ///  Trust persistence failed while the pending credential remained
    ///  retryable.
    kPersistenceFailed,
    ///  A matching pending credential was consumed but an administrative
    ///  mutation invalidated its trust fence before persistence.
    kInvalidated,
};

//  ---- Known device ----

///  The four durable states one persistent `KnownDeviceRecord` can occupy. A
///  Known Device is always in exactly one of these; only `kTrusted` carries a
///  usable credential.
enum class KnownDeviceState {
    ///  Holds a usable credential and can authenticate normally.
    kTrusted,
    ///  Trust was removed; the device may re-pair to become `kTrusted` again under
    ///  the same
    ///  identity.
    kRevoked,
    ///  Rejected at `hello` and at pairing; requires an explicit unblock before it
    ///  can do either
    ///  again.
    kBlocked,
    ///  No longer blocked but not yet re-paired; requires a completely fresh
    ///  pairing flow to become
    ///  `kTrusted` again.
    kUnpaired,
};

///  Outcome of `TrustStore::Block`.
enum class BlockOutcome {
    ///  The device transitioned from `kTrusted` or `kRevoked` to `kBlocked`.
    kBlocked,
    ///  The device was already `kBlocked`; nothing changed.
    kAlreadyBlocked,
    ///  A known device record exists for the identity, but its current state
    ///  (`kUnpaired`) is not
    ///  eligible for blocking in this phase.
    kNotEligible,
    ///  No known device record exists for the identity at all.
    kNotFound,
    ///  The transition was valid but the underlying persistence `Save` failed;
    ///  in-memory state was
    ///  rolled back.
    kSaveFailed,
};

///  Outcome of `TrustStore::Unblock`.
enum class UnblockOutcome {
    ///  The device transitioned from `kBlocked` to `kUnpaired`.
    kUnblocked,
    ///  A known device record exists for the identity, but it is not currently
    ///  `kBlocked`.
    kNotBlocked,
    ///  No known device record exists for the identity at all.
    kNotFound,
    ///  The transition was valid but the underlying persistence `Save` failed;
    ///  in-memory state was
    ///  rolled back.
    kSaveFailed,
};

///  Outcome of `TrustStore::Forget`.
enum class ForgetOutcome {
    ///  The device record was deleted entirely; its `shortId` is now free for
    ///  reuse.
    kForgotten,
    ///  A known device record exists for the identity, but its current state
    ///  (`kTrusted` or
    ///  `kBlocked`) is not eligible to be forgotten -- revoke or unblock it first.
    kNotEligible,
    ///  No known device record exists for the identity at all.
    kNotFound,
    ///  The transition was valid but the underlying persistence `Save` failed;
    ///  in-memory state was
    ///  rolled back.
    kSaveFailed,
};

///  Outcome of `TrustStore::Rename`.
enum class RenameOutcome {
    ///  The device's `displayName` was updated (or cleared, for an empty input).
    kRenamed,
    ///  The supplied display name exceeds the configured length bound or contains
    ///  a control
    ///  character.
    kInvalidDisplayName,
    ///  A known device record exists for the identity, but its current state is
    ///  not `kTrusted` --
    ///  only a currently trusted device may rename itself.
    kNotEligible,
    ///  No known device record exists for the identity at all.
    kNotFound,
    ///  The rename was valid but the underlying persistence `Save` failed;
    ///  in-memory state was
    ///  rolled back.
    kSaveFailed,
};

//  ---- Factory reset ----

///  Outcome of `FactoryResetChallenge::TryConfirm`. Unlike `ConfirmResult`,
///  there is no pacing or wrong-attempt budget: Factory Reset is a single local
///  admin operation, and `kInvalid` itself destroys the challenge outright
///  rather than counting toward a limit.
enum class FactoryResetConfirmOutcome {
    ///  The code matched and was consumed; the caller may now execute the
    ///  destructive wipe.
    kConfirmed,
    ///  No challenge is currently active -- it was never started, already expired,
    ///  or was already
    ///  consumed or invalidated by an earlier attempt.
    kExpired,
    ///  A challenge is active, but the presented code did not match it; the
    ///  challenge is destroyed
    ///  as part of this outcome, requiring a fresh `TryStart` to try again.
    kInvalid,
};

} //  namespace dovahlink::security

namespace dovahlink::transport {

//  ---- Transport ----

///  Identifies WebSocket session operation failures.
enum class SessionError {
    ///  The WebSocket upgrade handshake failed.
    kHandshakeFailed,
    ///  A message could not be read.
    kReadFailed,
    ///  A message could not be written.
    kWriteFailed,
    ///  A binary frame was received where text is required.
    kBinaryFrameRejected,
    ///  The inbound frame exceeded the configured size limit and must be closed
    ///  immediately rather than routed through post-decode violation handling.
    kFrameTooLarge,
};

///  Identifies listener setup failures.
enum class ListenerError {
    ///  The loopback address could not be opened, bound, or started.
    kBindFailed,
};

///  Identifies failures while accepting or validating a connection.
enum class AcceptError {
    ///  The TCP accept operation failed.
    kAcceptFailed,
    ///  The peer was not confirmed to be a loopback address.
    kNonLoopbackPeerRejected,
};

} //  namespace dovahlink::transport

namespace dovahlink::protocol {

//  ---- Protocol ----

///  Identifies the first input-size or JSON-shape limit violated during parsing.
enum class BoundedJsonError {
    ///  The complete inbound frame exceeds the configured byte limit.
    kFrameTooLarge,
    ///  The input is not valid JSON or exceeds the parser's nesting limit.
    kInvalidJson,
    ///  A JSON string or object key exceeds the configured byte limit.
    kStringTooLong,
    ///  A JSON array exceeds the configured item limit.
    kArrayTooLong,
    ///  A JSON object exceeds the configured member limit.
    kTooManyObjectMembers,
};

} //  namespace dovahlink::protocol

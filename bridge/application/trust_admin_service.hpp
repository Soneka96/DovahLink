#pragma once

#include "application/active_session_disconnector.hpp"
#include "security/factory_reset_challenge.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_store.hpp"

#include <chrono>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Reusable trust-administration behavior (list/revoke/reset/block/unblock)
///  over `security::TrustStore`, shared by every administration surface --
///  console, a future Flutter management UI, and developer tooling -- so none of
///  them duplicates trust-store logic
///  (`ai/context/protocol/security.md`'s "Trust administration surface"). Also
///  enforces that section's "Revocation is immediate" guarantee, extended
///  uniformly to blocking and both bulk operations: a successful
///  `RevokeByShortId`/`ConfirmFactoryReset`/`ResetTrust`/`BlockByShortId`
///  force-closes the affected client's active session through the injected
///  `ActiveSessionDisconnector`, not just the persisted trust record, and a
///  successful `BlockByShortId`/`ConfirmFactoryReset`/`ResetTrust` also cancels
///  any pairing challenge or pending credential in progress through the injected
///  `PairingSession`.
///
///  Known limitation: `RevokeByShortId`/`BlockByShortId`/`UnblockByShortId` each
///  call `TrustStore` more than once (find-then-act); each individual call is
///  atomic but the pair is not, so a concurrent `Persist`/`Revoke`/`Block` from
///  an unrelated connection between them could make a reported message (client
///  count, display name) stale by the time it prints. `TrustStore`'s own
///  persisted state always stays internally consistent regardless -- only this
///  service's human-readable report could rarely lag. Acceptable because this
///  service is driven by one serialized admin operator at a time (a console
///  command); add a lock spanning both calls if a second concurrent admin
///  surface is ever introduced.
class TrustAdminService {
  public:
    ///  Binds the service to the trust store it administers, the pairing session
    ///  it cancels owned or all pending challenges through, the Factory Reset
    ///  confirmation challenge it starts and confirms, and the session
    ///  disconnector it enforces immediate revocation/blocking through.
    ///  @param trustStore Persistent trust store this service reads and mutates.
    ///  @param sessionDisconnector Force-closes the active session on a successful
    ///  revoke, block,
    ///      Reset Trust, or Factory Reset.
    ///  @param pairingSession Cancels any pairing challenge or pending credential
    ///  the blocked
    ///      identity owns on a successful block, or every pairing
    ///      challenge/pending credential in progress on a successful Reset Trust
    ///      or Factory Reset.
    ///  @param factoryResetChallenge The six-digit confirmation challenge
    ///  `StartFactoryReset` and
    ///      `ConfirmFactoryReset` start and confirm.
    TrustAdminService(security::TrustStore& trustStore,
                      ActiveSessionDisconnector& sessionDisconnector,
                      security::PairingSession& pairingSession,
                      security::FactoryResetChallenge& factoryResetChallenge);

    ///  Lists known devices according to a console-facing scope: empty or
    ///  <c>all</c> for every known device, <c>trust</c> for trusted devices, or
    ///  <c>block</c> for blocked devices.
    ///  @param scope The requested listing scope.
    ///  @return A display-ready listing or a clear invalid-scope message.
    [[nodiscard]] std::string List(std::string_view scope) const;

    ///  Returns the canonical trust-administration console commands.
    ///  @return A display-ready multi-line help string.
    [[nodiscard]] std::string Help() const;

    ///  Lists every currently trusted client as one display-ready, multi-line
    ///  string: one `shortId  displayName` line per client, with temporary `#1`,
    ///  `#2`, ... suffixes when display names repeat, or a clear "no trusted
    ///  clients" message when empty.
    [[nodiscard]] std::string ListTrusted() const;

    ///  Lists every known device, regardless of state, sorted oldest-to-newest by
    ///  `createdAt` and including its `shortId`, display name, and current state.
    ///  Repeated display names receive a temporary oldest-to-newest `#1`, `#2`,
    ///  ... suffix only in this returned presentation.
    [[nodiscard]] std::string ListKnownDevices() const;

    ///  Lists only currently blocked known devices, using the same sorted,
    ///  presentation-only name disambiguation as `ListKnownDevices`.
    [[nodiscard]] std::string ListBlocked() const;

    ///  Revokes the trusted client identified by its five-digit `shortId` (never
    ///  `clientId`, never a credential -- `security.md`'s own stated purpose for
    ///  `shortId`).
    ///  @param shortId Administration-only identifier presented by the caller.
    ///  @return A human-readable result message: not-found, revoked, or a
    ///  persistence failure.
    [[nodiscard]] std::string RevokeByShortId(std::string_view shortId) const;

    ///  Starts a Factory Reset confirmation challenge: generates and displays a
    ///  six-digit code through the returned message. Performs no mutation,
    ///  disconnects no one, and alters no trust -- the destructive wipe happens
    ///  only once `ConfirmFactoryReset` receives the matching code. Always
    ///  (re)starts, discarding any previously started, unconfirmed challenge.
    ///  @return A human-readable message containing the code to display, or a
    ///  generator-failure
    ///      message.
    [[nodiscard]] std::string StartFactoryReset() const;

    ///  Blocks the known device identified by its five-digit `shortId`, regardless
    ///  of its current state (unlike `RevokeByShortId`, which only ever finds a
    ///  currently-trusted device). On success, also cancels any pairing challenge
    ///  the device owns and force-closes its active session.
    ///  @param shortId Administration-only identifier presented by the caller.
    ///  @param now Current monotonic time, forwarded to
    ///  `PairingSession::TryCancel`'s lazy-expiry
    ///      checks.
    ///  @return A human-readable result message: not-found, not-eligible,
    ///  already-blocked, blocked,
    ///      or a persistence failure.
    [[nodiscard]] std::string
    BlockByShortId(std::string_view shortId,
                   std::chrono::steady_clock::time_point now) const;

    ///  Unblocks the known device identified by its five-digit `shortId`,
    ///  returning it to `kUnpaired` without restoring its old credential.
    ///  @param shortId Administration-only identifier presented by the caller.
    ///  @return A human-readable result message: not-found, not-blocked,
    ///  unblocked, or a
    ///      persistence failure.
    [[nodiscard]] std::string UnblockByShortId(std::string_view shortId) const;

    ///  Forgets the known device identified by its five-digit `shortId`, deleting
    ///  its record entirely. Only a currently `kRevoked` or `kUnpaired` device is
    ///  eligible -- a `kTrusted` device must be revoked first, and a `kBlocked`
    ///  device must be explicitly unblocked first.
    ///  @param shortId Administration-only identifier presented by the caller.
    ///  @return A human-readable result message: not-found, not-eligible,
    ///  forgotten, or a
    ///      persistence failure.
    [[nodiscard]] std::string ForgetByShortId(std::string_view shortId) const;

    ///  Confirms a started Factory Reset challenge with the presented code. On a
    ///  match, executes the destructive wipe (every trusted credential, every
    ///  Known Device record, every block, every pending pairing) and disconnects
    ///  every active session. An expired or wrong confirmation performs zero
    ///  destructive changes; a wrong code also destroys the challenge immediately,
    ///  requiring a fresh `StartFactoryReset`.
    ///  @param presentedCode The code the operator entered.
    ///  @return A human-readable result message: wiped, expired, invalid, or a
    ///  persistence
    ///      failure.
    [[nodiscard]] std::string
    ConfirmFactoryReset(std::string_view presentedCode) const;

    ///  Reset Trust: immediately revokes every currently trusted device and
    ///  disconnects the active session, while preserving every Known Device record
    ///  (including blocks) and requiring no destructive-confirmation flow. Also
    ///  cancels every pairing challenge or pending credential in progress,
    ///  matching Factory Reset's own pairing-cancellation guarantee.
    ///  @return A human-readable result message naming how many devices were
    ///  revoked, or a
    ///      persistence failure.
    [[nodiscard]] std::string ResetTrust() const;

  private:
    ///  Trust store this service reads and mutates.
    security::TrustStore& trustStore_;

    ///  Force-closes the active session on a successful revoke, block, Reset
    ///  Trust, or Factory Reset.
    ActiveSessionDisconnector& sessionDisconnector_;

    ///  Cancels any pairing challenge or pending credential a blocked identity
    ///  owns, or every one in progress on a successful Reset Trust or Factory
    ///  Reset.
    security::PairingSession& pairingSession_;

    ///  The Factory Reset confirmation challenge `StartFactoryReset` and
    ///  `ConfirmFactoryReset` start and confirm.
    security::FactoryResetChallenge& factoryResetChallenge_;
};

} //  namespace dovahlink::application

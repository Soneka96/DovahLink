#pragma once

#include "application/trust_device_admin_service.hpp"
#include "application/trust_reset_service.hpp"

#include <chrono>
#include <string>
#include <string_view>

#include <chrono>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Composes the focused device-administration and reset services for callers
///  that still expose one trust-administration surface.
class TrustAdminService {
  public:
    ///  Binds both focused trust-administration services to their ports.
    ///  @param deviceStore Per-device trust store operations.
    ///  @param resetStore Bulk trust reset operations over the same authoritative
    ///      trust store as `deviceStore`.
    ///  @param sessionDisconnector Disconnects sessions affected by administration.
    ///  @param pairingCancellation Cancels active pairing state affected by
    ///      administration.
    ///  @param factoryResetChallenge The local Factory Reset confirmation
    ///      challenge.
    TrustAdminService(security::ITrustDeviceStore& deviceStore,
                      security::ITrustResetStore& resetStore,
                      ActiveSessionDisconnector& sessionDisconnector,
                      security::IPairingCancellation& pairingCancellation,
                      security::IFactoryResetChallenge& factoryResetChallenge);

    ///  Binds both focused services to the repository's concrete implementations.
    TrustAdminService(security::TrustStore& trustStore,
                      ActiveSessionDisconnector& sessionDisconnector,
                      security::PairingSession& pairingSession,
                      security::FactoryResetChallenge& factoryResetChallenge);

    ///  Lists known devices according to a console-facing scope.
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

    ///  Starts a Factory Reset confirmation challenge.
    ///  @return A human-readable message containing the code to display, or a
    ///  generator-failure
    ///      message.
    [[nodiscard]] std::string StartFactoryReset() const;

    ///  Blocks the known device identified by its administration short ID.
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

    ///  Confirms a started Factory Reset challenge and performs the destructive
    ///  wipe on success.
    ///  @param presentedCode The code the operator entered.
    ///  @return A human-readable result message: wiped, expired, invalid, or a
    ///  persistence
    ///      failure.
    [[nodiscard]] std::string
    ConfirmFactoryReset(std::string_view presentedCode) const;

    ///  Performs the recoverable Reset Trust workflow.
    ///  @return A human-readable result message naming how many devices were
    ///  revoked, or a
    ///      persistence failure.
    [[nodiscard]] std::string ResetTrust() const;

  private:
    ///  Focused device listing and mutation behavior.
    TrustDeviceAdminService deviceService_;

    ///  Focused trust-reset and Factory Reset behavior.
    TrustResetService resetService_;
};

} //  namespace dovahlink::application

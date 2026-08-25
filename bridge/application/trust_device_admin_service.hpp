#pragma once

#include "application/active_session_disconnector.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "security/trust_store.hpp"

#include <chrono>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Coordinates listing and individual Known Device administration without
///  owning trust persistence or transport state.
class TrustDeviceAdminService {
  public:
    ///  Binds per-device trust operations to session invalidation and the
    ///  shared trust-mutation coordination boundary.
    ///  @param deviceStore Per-device trust-store operations.
    ///  @param sessionDisconnector Disconnects the affected client session.
    ///  @param mutationCoordinator Serializes blocking and revocation with pairing
    ///  finalization.
    TrustDeviceAdminService(
        security::ITrustDeviceStore& deviceStore,
        ActiveSessionDisconnector& sessionDisconnector,
        ITrustMutationCoordinator& mutationCoordinator);

    ///  Lists known devices according to the console-facing scope.
    [[nodiscard]] std::string List(std::string_view scope) const;

    ///  Returns the canonical trust-administration console commands.
    [[nodiscard]] std::string Help() const;

    ///  Lists every currently trusted client.
    [[nodiscard]] std::string ListTrusted() const;

    ///  Lists every known device regardless of state.
    [[nodiscard]] std::string ListKnownDevices() const;

    ///  Lists only currently blocked known devices.
    [[nodiscard]] std::string ListBlocked() const;

    ///  Revokes the trusted client identified by its administration short ID.
    [[nodiscard]] std::string
    RevokeByShortId(std::string_view shortId,
                    std::chrono::steady_clock::time_point now) const;

    ///  Blocks the known device identified by its administration short ID.
    [[nodiscard]] std::string
    BlockByShortId(std::string_view shortId,
                   std::chrono::steady_clock::time_point now) const;

    ///  Unblocks the known device identified by its administration short ID.
    [[nodiscard]] std::string UnblockByShortId(std::string_view shortId) const;

    ///  Forgets an eligible known device identified by its administration short
    ///  ID.
    [[nodiscard]] std::string ForgetByShortId(std::string_view shortId) const;

  private:
    ///  Per-device trust-store operations.
    security::ITrustDeviceStore& deviceStore_;

    ///  Disconnects a session after a successful revoke or block.
    ActiveSessionDisconnector& sessionDisconnector_;

    ///  Coordinates blocking and revocation with pairing finalization.
    ITrustMutationCoordinator& mutationCoordinator_;
};

} //  namespace dovahlink::application

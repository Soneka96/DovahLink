#pragma once

#include "application/active_session_disconnector.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "security/trust_device_store.hpp"

#include <chrono>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Provides the reusable Known Device administration capability.
class ITrustDeviceAdminService {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustDeviceAdminService() = default;

    ///  Lists known devices according to the console-facing scope.
    [[nodiscard]] virtual std::string List(std::string_view scope) const = 0;

    ///  Returns the canonical trust-administration console commands.
    [[nodiscard]] virtual std::string Help() const = 0;

    ///  Revokes the trusted client identified by its administration short ID.
    [[nodiscard]] virtual std::string
    RevokeByShortId(std::string_view shortId,
                    std::chrono::steady_clock::time_point now) const = 0;

    ///  Blocks the known device identified by its administration short ID.
    [[nodiscard]] virtual std::string
    BlockByShortId(std::string_view shortId,
                   std::chrono::steady_clock::time_point now) const = 0;

    ///  Unblocks the known device identified by its administration short ID.
    [[nodiscard]] virtual std::string
    UnblockByShortId(std::string_view shortId) const = 0;

    ///  Forgets an eligible known device identified by its administration short ID.
    [[nodiscard]] virtual std::string
    ForgetByShortId(std::string_view shortId) const = 0;
};

///  Coordinates listing and individual Known Device administration without
///  owning trust persistence or transport state.
class TrustDeviceAdminService final : public ITrustDeviceAdminService {
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
    [[nodiscard]] std::string List(std::string_view scope) const override;

    ///  Returns the canonical trust-administration console commands.
    [[nodiscard]] std::string Help() const override;

    ///  Revokes the trusted client identified by its administration short ID.
    [[nodiscard]] std::string
    RevokeByShortId(std::string_view shortId,
                    std::chrono::steady_clock::time_point now) const override;

    ///  Blocks the known device identified by its administration short ID.
    [[nodiscard]] std::string
    BlockByShortId(std::string_view shortId,
                   std::chrono::steady_clock::time_point now) const override;

    ///  Unblocks the known device identified by its administration short ID.
    [[nodiscard]] std::string
    UnblockByShortId(std::string_view shortId) const override;

    ///  Forgets an eligible known device identified by its administration short
    ///  ID.
    [[nodiscard]] std::string
    ForgetByShortId(std::string_view shortId) const override;

  private:
    ///  Lists every currently trusted client for the scoped public operation.
    [[nodiscard]] std::string ListTrusted() const;

    ///  Lists every known device for the scoped public operation.
    [[nodiscard]] std::string ListKnownDevices() const;

    ///  Lists only currently blocked known devices for the scoped public operation.
    [[nodiscard]] std::string ListBlocked() const;

    ///  Per-device trust-store operations.
    security::ITrustDeviceStore& deviceStore_;

    ///  Disconnects a session after a successful revoke or block.
    ActiveSessionDisconnector& sessionDisconnector_;

    ///  Coordinates blocking and revocation with pairing finalization.
    ITrustMutationCoordinator& mutationCoordinator_;
};

} //  namespace dovahlink::application

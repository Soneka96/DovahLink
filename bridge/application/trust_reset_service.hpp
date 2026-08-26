#pragma once

#include "application/active_session_disconnector.hpp"
#include "application/trust_mutation_coordinator.hpp"
#include "security/factory_reset_challenge.hpp"
#include "security/trust_reset_store.hpp"

#include <string>
#include <string_view>

namespace dovahlink::application {

///  Provides the reusable trust reset and Factory Reset capability.
class ITrustResetService {
  public:
    ///  Releases the interface without performing work.
    virtual ~ITrustResetService() = default;

    ///  Starts a Factory Reset confirmation challenge.
    [[nodiscard]] virtual std::string StartFactoryReset() const = 0;

    ///  Confirms a Factory Reset challenge and performs the destructive wipe.
    [[nodiscard]] virtual std::string
    ConfirmFactoryReset(std::string_view presentedCode) const = 0;

    ///  Revokes every trusted device while preserving Known Device records.
    [[nodiscard]] virtual std::string ResetTrust() const = 0;
};

///  Coordinates recoverable and destructive trust reset workflows.
class TrustResetService final : public ITrustResetService {
  public:
    ///  Binds bulk trust operations to coordinated pairing cancellation,
    ///  session invalidation, and the local Factory Reset challenge.
    ///  @param resetStore Bulk trust-store operations.
    ///  @param sessionDisconnector Disconnects sessions affected by a reset.
    ///  @param mutationCoordinator Serializes reset mutations with pairing.
    ///  @param factoryResetChallenge Starts and confirms the local reset code.
    TrustResetService(
        security::ITrustResetStore& resetStore,
        IActiveSessionDisconnector& sessionDisconnector,
        ITrustMutationCoordinator& mutationCoordinator,
        security::IFactoryResetChallenge& factoryResetChallenge);

    ///  Starts a Factory Reset confirmation challenge.
    [[nodiscard]] std::string StartFactoryReset() const override;

    ///  Confirms a Factory Reset challenge and performs the destructive wipe.
    [[nodiscard]] std::string ConfirmFactoryReset(
        std::string_view presentedCode) const override;

    ///  Revokes every trusted device while preserving Known Device records.
    [[nodiscard]] std::string ResetTrust() const override;

  private:
    ///  Bulk trust-store operations.
    security::ITrustResetStore& resetStore_;

    ///  Disconnects targeted or all active sessions after reset.
    IActiveSessionDisconnector& sessionDisconnector_;

    ///  Coordinates reset mutations with active pairing state.
    ITrustMutationCoordinator& mutationCoordinator_;

    ///  Local confirmation challenge for destructive reset.
    security::IFactoryResetChallenge& factoryResetChallenge_;
};

} //  namespace dovahlink::application

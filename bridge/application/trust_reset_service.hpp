#pragma once

#include "application/active_session_disconnector.hpp"
#include "security/factory_reset_challenge.hpp"
#include "security/pairing_session.hpp"
#include "security/trust_store.hpp"

#include <string>
#include <string_view>

namespace dovahlink::application {

///  Coordinates recoverable and destructive trust reset workflows.
class TrustResetService {
  public:
    ///  Binds bulk trust-store operations to pairing cancellation, session
    ///  invalidation, and the local Factory Reset challenge.
    ///  @param resetStore Bulk trust-store operations.
    ///  @param sessionDisconnector Disconnects sessions affected by a reset.
    ///  @param pairingCancellation Cancels active pairing state after a reset.
    ///  @param factoryResetChallenge Starts and confirms the local reset code.
    TrustResetService(
        security::ITrustResetStore& resetStore,
        ActiveSessionDisconnector& sessionDisconnector,
        security::IPairingCancellation& pairingCancellation,
        security::IFactoryResetChallenge& factoryResetChallenge);

    ///  Starts a Factory Reset confirmation challenge.
    [[nodiscard]] std::string StartFactoryReset() const;

    ///  Confirms a Factory Reset challenge and performs the destructive wipe.
    [[nodiscard]] std::string
    ConfirmFactoryReset(std::string_view presentedCode) const;

    ///  Revokes every trusted device while preserving Known Device records.
    [[nodiscard]] std::string ResetTrust() const;

  private:
    ///  Bulk trust-store operations.
    security::ITrustResetStore& resetStore_;

    ///  Disconnects targeted or all active sessions after reset.
    ActiveSessionDisconnector& sessionDisconnector_;

    ///  Cancels active pairing state after reset.
    security::IPairingCancellation& pairingCancellation_;

    ///  Local confirmation challenge for destructive reset.
    security::IFactoryResetChallenge& factoryResetChallenge_;
};

} //  namespace dovahlink::application

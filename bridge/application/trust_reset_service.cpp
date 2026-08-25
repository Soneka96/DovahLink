#include "application/trust_reset_service.hpp"

#include <string>

namespace dovahlink::application {

TrustResetService::TrustResetService(
    security::ITrustResetStore& resetStore,
    ActiveSessionDisconnector& sessionDisconnector,
    security::IPairingCancellation& pairingCancellation,
    security::IFactoryResetChallenge& factoryResetChallenge)
    : resetStore_(resetStore), sessionDisconnector_(sessionDisconnector),
      pairingCancellation_(pairingCancellation),
      factoryResetChallenge_(factoryResetChallenge) {}

std::string TrustResetService::StartFactoryReset() const {
    auto code = factoryResetChallenge_.TryStart();
    if (!code.has_value()) {
        return "Failed to start Factory Reset: could not generate a confirmation "
               "code.";
    }
    return "Factory Reset requested. Confirm with code " + *code +
           " within 60 seconds to permanently erase all trust.";
}

std::string
TrustResetService::ConfirmFactoryReset(std::string_view presentedCode) const {
    switch (factoryResetChallenge_.TryConfirm(std::string(presentedCode))) {
    case security::FactoryResetConfirmOutcome::kConfirmed: {
        std::size_t previousCount = resetStore_.ListTrusted().size();
        if (!resetStore_.Reset()) {
            return "Failed to complete Factory Reset: trust-store save failed.";
        }
        pairingCancellation_.CancelAll();
        sessionDisconnector_.DisconnectActive("factory_reset");
        return "Factory Reset complete (" + std::to_string(previousCount) +
               (previousCount == 1 ? " trusted device erased)."
                                   : " trusted devices erased).");
    }
    case security::FactoryResetConfirmOutcome::kExpired:
        return "No Factory Reset confirmation is pending; start one with 'reset' "
               "first.";
    case security::FactoryResetConfirmOutcome::kInvalid:
        return "Wrong Factory Reset confirmation code; the challenge was "
               "cancelled. Start over with 'reset'.";
    }
    return "Failed to confirm Factory Reset: unexpected outcome.";
}

std::string TrustResetService::ResetTrust() const {
    auto previouslyTrusted = resetStore_.ListTrusted();
    if (!resetStore_.ResetTrust()) {
        return "Failed to reset trust: trust-store save failed.";
    }
    pairingCancellation_.CancelAll();
    for (const auto& record : previouslyTrusted) {
        sessionDisconnector_.DisconnectIfClientActive(record.clientId,
                                                      "trust_reset");
    }
    return "Reset Trust complete (" + std::to_string(previouslyTrusted.size()) +
           (previouslyTrusted.size() == 1 ? " device revoked)."
                                          : " devices revoked).");
}

} //  namespace dovahlink::application

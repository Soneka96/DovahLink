#include "application/trust_reset_service.hpp"

#include <chrono>
#include <string>

namespace dovahlink::application {

TrustResetService::TrustResetService(
    security::ITrustResetStore& resetStore,
    ActiveSessionDisconnector& sessionDisconnector,
    ITrustMutationCoordinator& mutationCoordinator,
    security::IFactoryResetChallenge& factoryResetChallenge)
    : resetStore_(resetStore), sessionDisconnector_(sessionDisconnector),
      mutationCoordinator_(mutationCoordinator),
      factoryResetChallenge_(factoryResetChallenge) {}

std::string TrustResetService::StartFactoryReset() const {
    auto code = factoryResetChallenge_.TryStart();
    if (!code.has_value()) {
        return "Failed to start Factory Reset: could not generate a confirmation "
               "code.";
    }
    auto ttlSeconds = std::chrono::ceil<std::chrono::seconds>(
        factoryResetChallenge_.CodeTimeToLive());
    if (ttlSeconds.count() < 0) {
        ttlSeconds = std::chrono::seconds(0);
    }
    return "Factory Reset requested. Confirm with code " + *code + " within " +
           std::to_string(ttlSeconds.count()) +
           " seconds to permanently erase all trust.";
}

std::string
TrustResetService::ConfirmFactoryReset(std::string_view presentedCode) const {
    switch (factoryResetChallenge_.TryConfirm(std::string(presentedCode))) {
    case security::FactoryResetConfirmOutcome::kConfirmed: {
        std::size_t previousCount = resetStore_.ListTrusted().size();
        if (!mutationCoordinator_.FactoryReset()) {
            return "Failed to complete Factory Reset: trust-store save failed.";
        }
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
    if (!mutationCoordinator_.ResetTrust()) {
        return "Failed to reset trust: trust-store save failed.";
    }
    for (const auto& record : previouslyTrusted) {
        sessionDisconnector_.DisconnectIfClientActive(record.clientId,
                                                      "trust_reset");
    }
    return "Reset Trust complete (" + std::to_string(previouslyTrusted.size()) +
           (previouslyTrusted.size() == 1 ? " device revoked)."
                                          : " devices revoked).");
}

} //  namespace dovahlink::application

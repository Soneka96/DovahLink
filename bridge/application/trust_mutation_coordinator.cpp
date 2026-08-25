#include "application/trust_mutation_coordinator.hpp"

#include <utility>

namespace dovahlink::application {

TrustMutationCoordinator::TrustMutationCoordinator(
    security::TrustStore& trustStore, security::PairingSession& pairingSession)
    : trustStore_(trustStore), pairingSession_(pairingSession) {}

security::ConfirmCodeResult TrustMutationCoordinator::ConfirmPairing(
    const std::string& presentedCode,
    std::chrono::steady_clock::time_point now, std::string clientId,
    std::vector<std::uint8_t> credential,
    std::optional<std::string> displayName) {
    std::lock_guard<std::mutex> lock(mutex_);
    const auto mutationGeneration =
        trustStore_.CurrentMutationGeneration(clientId);
    return pairingSession_.TryConfirmCode(
        presentedCode, now, std::move(clientId), std::move(credential),
        std::move(displayName), mutationGeneration);
}

PairingCommitResult TrustMutationCoordinator::CommitPairing(
    const std::string& clientId,
    const std::vector<std::uint8_t>& credential,
    std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto pending = pairingSession_.PeekPending(clientId, credential, now);
    if (!pending.has_value()) {
        return {.outcome = security::PairingCommitOutcome::kPendingNotFound};
    }

    if (!pairingSession_.CommitPending(clientId, credential, now)) {
        return {.outcome = security::PairingCommitOutcome::kPendingNotFound};
    }

    auto persisted = trustStore_.PersistIfGeneration(
        pending->mutationGeneration, pending->clientId,
        pending->credential, pending->displayName);
    if (persisted.has_value()) {
        return {.outcome = security::PairingCommitOutcome::kCommitted,
                .record = std::move(persisted)};
    }

    //  A generation change means an administrative operation invalidated this
    //  pending pairing. If the generation is unchanged, restore the consumed
    //  credential so a transient persistence failure remains retryable.
    if (trustStore_.CurrentMutationGeneration(pending->clientId) ==
        pending->mutationGeneration) {
        //  Pairing cancellation is coordinated by this same mutex in production,
        //  so restoration cannot race an administrative cancellation.
        (void)pairingSession_.RestorePending(std::move(*pending));
        return {.outcome = security::PairingCommitOutcome::kPersistenceFailed};
    }
    return {.outcome = security::PairingCommitOutcome::kInvalidated};
}

security::CancelOutcome TrustMutationCoordinator::TryCancel(
    const std::string& clientId, std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    return pairingSession_.TryCancel(clientId, now);
}

void TrustMutationCoordinator::CancelAll() {
    std::lock_guard<std::mutex> lock(mutex_);
    pairingSession_.CancelAll();
}

security::BlockOutcome TrustMutationCoordinator::Block(
    const std::string& clientId, std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto outcome = trustStore_.Block(clientId);
    if (outcome == security::BlockOutcome::kBlocked) {
        (void)pairingSession_.TryCancel(clientId, now);
    }
    return outcome;
}

bool TrustMutationCoordinator::ResetTrust() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!trustStore_.ResetTrust()) {
        return false;
    }
    pairingSession_.CancelAll();
    return true;
}

bool TrustMutationCoordinator::FactoryReset() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!trustStore_.Reset()) {
        return false;
    }
    pairingSession_.CancelAll();
    return true;
}

} //  namespace dovahlink::application

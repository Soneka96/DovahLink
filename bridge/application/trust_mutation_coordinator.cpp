#include "application/trust_mutation_coordinator.hpp"

#include <utility>

namespace dovahlink::application {

TrustMutationCoordinator::TrustMutationCoordinator(
    security::ITrustStore& trustStore,
    security::IPairingSession& pairingSession,
    ISessionManager& sessionManager)
    : trustStore_(trustStore), pairingSession_(pairingSession),
      sessionManager_(sessionManager) {}

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
    std::chrono::steady_clock::time_point now, ConnectionId connection,
    const std::string& sessionId) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto pending = pairingSession_.PeekPending(clientId, credential, now);
    if (!pending.has_value()) {
        return PairingCommitResult::PendingNotFound();
    }

    if (!pairingSession_.CommitPending(clientId, credential, now)) {
        return PairingCommitResult::PendingNotFound();
    }

    auto persisted = trustStore_.PersistIfGeneration(
        pending->mutationGeneration, pending->clientId,
        pending->credential, pending->displayName);
    if (persisted.has_value()) {
        sessionManager_.UpgradeToFullTrust(connection, sessionId);
        return PairingCommitResult::Committed(std::move(*persisted));
    }

    //  A generation change means an administrative operation invalidated this
    //  pending pairing. If the generation is unchanged, restore the consumed
    //  credential so a transient persistence failure remains retryable.
    if (trustStore_.CurrentMutationGeneration(pending->clientId) ==
        pending->mutationGeneration) {
        //  Pairing cancellation is coordinated by this same mutex in production,
        //  so restoration cannot race an administrative cancellation.
        (void)pairingSession_.RestorePending(std::move(*pending));
        return PairingCommitResult::PersistenceFailed();
    }
    return PairingCommitResult::Invalidated();
}

std::optional<security::KnownDeviceRecord>
TrustMutationCoordinator::PromoteAlreadyTrusted(
    const std::string& clientId, const std::vector<std::uint8_t>& credential,
    ConnectionId connection, const std::string& sessionId) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!trustStore_.Authenticate(clientId, credential)) {
        return std::nullopt;
    }
    auto existing = trustStore_.Query(clientId);
    if (!existing.has_value()) {
        return std::nullopt;
    }
    sessionManager_.UpgradeToFullTrust(connection, sessionId);
    return existing;
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

bool TrustMutationCoordinator::Revoke(
    const std::string& clientId, std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!trustStore_.Query(clientId).has_value()) {
        return true;
    }
    if (!trustStore_.Revoke(clientId)) {
        return false;
    }
    (void)pairingSession_.TryCancel(clientId, now);
    return true;
}

std::optional<std::vector<std::string>> TrustMutationCoordinator::ResetTrust() {
    std::lock_guard<std::mutex> lock(mutex_);
    const auto trustedDevices = trustStore_.ListTrusted();
    std::vector<std::string> affectedClientIds;
    affectedClientIds.reserve(trustedDevices.size());
    for (const auto& device : trustedDevices) {
        affectedClientIds.push_back(device.clientId);
    }

    if (!trustStore_.ResetTrust()) {
        return std::nullopt;
    }
    pairingSession_.CancelAll();
    return affectedClientIds;
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

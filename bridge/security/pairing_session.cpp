#include "security/pairing_session.hpp"

#include "security/constant_time_compare.hpp"
#include "security/csprng.hpp"
#include "security/limits.hpp"

#include <utility>

namespace dovahlink::security {

PairingSession::PairingSession(CodeGenerator codeGenerator, std::chrono::steady_clock::duration codeTimeToLive)
    : codeGenerator_(std::move(codeGenerator)), codeTimeToLive_(codeTimeToLive) {}

std::optional<std::string> PairingSession::DefaultCodeGenerator() {
    constexpr std::size_t kPairingCodeDigits = 6;
    return GenerateNumericCode(kPairingCodeDigits);
}

void PairingSession::ExpireOwnerIfGraceElapsedLocked(std::chrono::steady_clock::time_point now) {
    if (!disconnectedAt_.has_value()) {
        return;
    }
    if (now - *disconnectedAt_ >= kPairingReconnectGracePeriod) {
        activeChallenge_.reset();
        ownerClientId_.reset();
        disconnectedAt_.reset();
    }
}

PairingSession::StartChallengeResult PairingSession::TryStartChallenge(
    const std::string& clientId, std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    ExpireOwnerIfGraceElapsedLocked(now);

    if (pendingCredential_.has_value()) {
        if (pendingCredential_->clientId == clientId) {
            return {.outcome = StartChallengeOutcome::kResumed, .code = std::nullopt};
        }
        return {.outcome = StartChallengeOutcome::kOtherDeviceActive, .code = std::nullopt};
    }
    if (activeChallenge_.has_value() && activeChallenge_->IsAvailable()) {
        if (ownerClientId_.has_value() && *ownerClientId_ == clientId) {
            return {.outcome = StartChallengeOutcome::kResumed, .code = std::nullopt};
        }
        return {.outcome = StartChallengeOutcome::kOtherDeviceActive, .code = std::nullopt};
    }

    auto code = codeGenerator_();
    if (!code.has_value()) {
        return {.outcome = StartChallengeOutcome::kGeneratorFailed, .code = std::nullopt};
    }

    std::vector<std::uint8_t> codeBytes(code->begin(), code->end());
    activeChallenge_.emplace(std::move(codeBytes), codeTimeToLive_);
    ownerClientId_ = clientId;
    disconnectedAt_.reset();
    return {.outcome = StartChallengeOutcome::kStarted, .code = code};
}

std::optional<std::chrono::seconds> PairingSession::RemainingSeconds(
    std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    ExpireOwnerIfGraceElapsedLocked(now);
    if (!activeChallenge_.has_value()) {
        return std::nullopt;
    }
    return activeChallenge_->RemainingSeconds();
}

void PairingSession::NotifyDisconnected(const std::string& clientId,
                                         std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (activeChallenge_.has_value() && ownerClientId_.has_value() && *ownerClientId_ == clientId) {
        disconnectedAt_ = now;
    }
}

void PairingSession::NotifyReconnected(const std::string& clientId,
                                        std::chrono::steady_clock::time_point now) {
    std::lock_guard<std::mutex> lock(mutex_);
    ExpireOwnerIfGraceElapsedLocked(now);
    if (ownerClientId_.has_value() && *ownerClientId_ == clientId) {
        disconnectedAt_.reset();
    }
}

PairingSession::ConfirmResult PairingSession::TryConfirmCode(const std::string& presentedCode,
                                                              std::chrono::steady_clock::time_point now,
                                                              std::string clientId,
                                                              std::vector<std::uint8_t> credential,
                                                              std::optional<std::string> displayName) {
    std::lock_guard<std::mutex> lock(mutex_);
    ExpireOwnerIfGraceElapsedLocked(now);
    if (codeAttemptThrottle_.IsBlocked(now)) {
        return ConfirmResult::kRateLimited;
    }
    if (!activeChallenge_.has_value()) {
        return ConfirmResult::kInvalid;
    }
    // Indistinguishable from a wrong code to a non-owner: reveals nothing about the challenge or
    // its real owner, and never even reaches TryReserve to consume an attempt against it.
    if (ownerClientId_.has_value() && *ownerClientId_ != clientId) {
        return ConfirmResult::kInvalid;
    }
    // Checked ahead of TryReserve (which would itself fail identically either way) so a caller
    // can be told "expired" instead of the less useful "invalid".
    if (!activeChallenge_->IsAvailable()) {
        return ConfirmResult::kExpired;
    }

    std::vector<std::uint8_t> presentedBytes(presentedCode.begin(), presentedCode.end());
    auto reservation = activeChallenge_->TryReserve(presentedBytes);
    if (!reservation.has_value()) {
        codeAttemptThrottle_.RecordFailure(now);
        return ConfirmResult::kInvalid;
    }

    reservation->Commit();
    activeChallenge_.reset();
    ownerClientId_.reset();
    pendingCredential_ = PendingCredential{
        .clientId = std::move(clientId),
        .credential = std::move(credential),
        .displayName = std::move(displayName),
    };
    return ConfirmResult::kConfirmed;
}

std::optional<PendingCredential> PairingSession::PeekPending(const std::string& clientId,
                                                               const std::vector<std::uint8_t>& credential) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!pendingCredential_.has_value()) {
        return std::nullopt;
    }
    if (pendingCredential_->clientId != clientId ||
        !ConstantTimeEquals(pendingCredential_->credential, credential)) {
        return std::nullopt;
    }
    return pendingCredential_;
}

bool PairingSession::CommitPending(const std::string& clientId, const std::vector<std::uint8_t>& credential) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!pendingCredential_.has_value()) {
        return false;
    }
    if (pendingCredential_->clientId != clientId ||
        !ConstantTimeEquals(pendingCredential_->credential, credential)) {
        return false;
    }
    pendingCredential_.reset();
    return true;
}

}  // namespace dovahlink::security

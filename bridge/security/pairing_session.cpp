#include "security/pairing_session.hpp"

#include "security/constant_time_compare.hpp"
#include "security/csprng.hpp"

#include <utility>

namespace dovahlink::security {

PairingSession::PairingSession(CodeGenerator codeGenerator, std::chrono::steady_clock::duration codeTimeToLive)
    : codeGenerator_(std::move(codeGenerator)), codeTimeToLive_(codeTimeToLive) {}

std::optional<std::string> PairingSession::DefaultCodeGenerator() {
    constexpr std::size_t kPairingCodeDigits = 6;
    return GenerateNumericCode(kPairingCodeDigits);
}

PairingSession::StartChallengeResult PairingSession::TryStartChallenge() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (pendingCredential_.has_value()) {
        return {.outcome = StartChallengeOutcome::kAlreadyInProgress, .code = std::nullopt};
    }
    if (activeChallenge_.has_value() && activeChallenge_->IsAvailable()) {
        return {.outcome = StartChallengeOutcome::kAlreadyInProgress, .code = std::nullopt};
    }

    auto code = codeGenerator_();
    if (!code.has_value()) {
        return {.outcome = StartChallengeOutcome::kGeneratorFailed, .code = std::nullopt};
    }

    std::vector<std::uint8_t> codeBytes(code->begin(), code->end());
    activeChallenge_.emplace(std::move(codeBytes), codeTimeToLive_);
    return {.outcome = StartChallengeOutcome::kStarted, .code = code};
}

PairingSession::ConfirmResult PairingSession::TryConfirmCode(const std::string& presentedCode,
                                                              std::chrono::steady_clock::time_point now,
                                                              std::string clientId,
                                                              std::vector<std::uint8_t> credential,
                                                              std::optional<std::string> displayName) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (codeAttemptThrottle_.IsBlocked(now)) {
        return ConfirmResult::kRateLimited;
    }
    if (!activeChallenge_.has_value()) {
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
    pendingCredential_ = PendingCredential{
        .clientId = std::move(clientId),
        .credential = std::move(credential),
        .displayName = std::move(displayName),
    };
    return ConfirmResult::kConfirmed;
}

std::optional<PendingCredential> PairingSession::TryFinalize(const std::string& clientId,
                                                               const std::vector<std::uint8_t>& credential) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!pendingCredential_.has_value()) {
        return std::nullopt;
    }
    if (pendingCredential_->clientId != clientId ||
        !ConstantTimeEquals(pendingCredential_->credential, credential)) {
        return std::nullopt;
    }

    auto result = std::move(*pendingCredential_);
    pendingCredential_.reset();
    return result;
}

}  // namespace dovahlink::security

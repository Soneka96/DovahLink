#include "security/pairing_session.hpp"

#include "security/csprng.hpp"

#include <utility>

namespace dovahlink::security {

PairingSession::PairingSession(CodeGenerator codeGenerator, std::chrono::steady_clock::duration codeTimeToLive)
    : codeGenerator_(std::move(codeGenerator)), codeTimeToLive_(codeTimeToLive) {}

std::optional<std::string> PairingSession::DefaultCodeGenerator() {
    constexpr std::size_t kPairingCodeDigits = 6;
    return GenerateNumericCode(kPairingCodeDigits);
}

std::optional<std::string> PairingSession::TryStartChallenge() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (pendingCredential_.has_value()) {
        return std::nullopt;
    }
    if (activeChallenge_.has_value() && activeChallenge_->IsAvailable()) {
        return std::nullopt;
    }

    auto code = codeGenerator_();
    if (!code.has_value()) {
        return std::nullopt;
    }

    std::vector<std::uint8_t> codeBytes(code->begin(), code->end());
    activeChallenge_.emplace(std::move(codeBytes), codeTimeToLive_);
    return code;
}

bool PairingSession::TryConfirmCode(const std::string& presentedCode, std::chrono::steady_clock::time_point now,
                                     std::string clientId, std::vector<std::uint8_t> credential,
                                     std::optional<std::string> displayName) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (codeAttemptThrottle_.IsBlocked(now)) {
        return false;
    }
    if (!activeChallenge_.has_value()) {
        return false;
    }

    std::vector<std::uint8_t> presentedBytes(presentedCode.begin(), presentedCode.end());
    auto reservation = activeChallenge_->TryReserve(presentedBytes);
    if (!reservation.has_value()) {
        codeAttemptThrottle_.RecordFailure(now);
        return false;
    }

    reservation->Commit();
    activeChallenge_.reset();
    pendingCredential_ = PendingCredential{
        .clientId = std::move(clientId),
        .credential = std::move(credential),
        .displayName = std::move(displayName),
    };
    return true;
}

std::optional<PendingCredential> PairingSession::TryFinalize(const std::string& clientId,
                                                               const std::vector<std::uint8_t>& credential) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!pendingCredential_.has_value()) {
        return std::nullopt;
    }
    if (pendingCredential_->clientId != clientId || pendingCredential_->credential != credential) {
        return std::nullopt;
    }

    auto result = std::move(*pendingCredential_);
    pendingCredential_.reset();
    return result;
}

}  // namespace dovahlink::security

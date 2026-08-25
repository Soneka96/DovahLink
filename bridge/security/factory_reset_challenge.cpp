#include "security/factory_reset_challenge.hpp"

#include "security/csprng.hpp"
#include "security/limits.hpp"

#include <utility>

namespace dovahlink::security {

FactoryResetChallenge::FactoryResetChallenge(
    CodeGenerator codeGenerator,
    std::chrono::steady_clock::duration codeTimeToLive)
    : codeGenerator_(std::move(codeGenerator)),
      codeTimeToLive_(codeTimeToLive) {}

std::optional<std::string> FactoryResetChallenge::DefaultCodeGenerator() {
  return GenerateNumericCode(kFactoryResetCodeDigits);
}

std::optional<std::string> FactoryResetChallenge::TryStart() {
  std::lock_guard<std::mutex> lock(mutex_);
  auto code = codeGenerator_();
  if (!code.has_value()) {
    activeChallenge_.reset();
    return std::nullopt;
  }

  std::vector<std::uint8_t> codeBytes(code->begin(), code->end());
  activeChallenge_.emplace(std::move(codeBytes), codeTimeToLive_);
  return code;
}

FactoryResetConfirmOutcome
FactoryResetChallenge::TryConfirm(const std::string &presentedCode) {
  std::lock_guard<std::mutex> lock(mutex_);
  if (!activeChallenge_.has_value() || !activeChallenge_->IsAvailable()) {
    activeChallenge_.reset();
    return FactoryResetConfirmOutcome::kExpired;
  }

  std::vector<std::uint8_t> presentedBytes(presentedCode.begin(),
                                           presentedCode.end());
  auto reservation = activeChallenge_->TryReserve(presentedBytes);
  if (!reservation.has_value()) {
    // One wrong attempt destroys the challenge immediately -- no multi-attempt
    // budget, unlike PairingSession's pairing code.
    activeChallenge_.reset();
    return FactoryResetConfirmOutcome::kInvalid;
  }

  reservation->Commit();
  activeChallenge_.reset();
  return FactoryResetConfirmOutcome::kConfirmed;
}

} // namespace dovahlink::security

#include "security/pairing_session.hpp"

#include "security/constant_time_compare.hpp"
#include "security/csprng.hpp"
#include "security/limits.hpp"

#include <utility>

namespace dovahlink::security {

PairingSession::PairingSession(
    CodeGenerator codeGenerator,
    std::chrono::steady_clock::duration codeTimeToLive)
    : codeGenerator_(std::move(codeGenerator)),
      codeTimeToLive_(codeTimeToLive) {}

std::optional<std::string> PairingSession::DefaultCodeGenerator() {
  constexpr std::size_t kPairingCodeDigits = 6;
  return GenerateNumericCode(kPairingCodeDigits);
}

void PairingSession::ExpireOwnerIfGraceElapsedLocked(
    std::chrono::steady_clock::time_point now) {
  if (!disconnectedAt_.has_value()) {
    return;
  }
  if (now - *disconnectedAt_ >= kPairingReconnectGracePeriod) {
    ClearChallengeLocked();
  }
}

void PairingSession::ExpirePendingIfElapsedLocked(
    std::chrono::steady_clock::time_point now) {
  if (!pendingCredential_.has_value()) {
    return;
  }
  if (now - pendingCredential_->pendingSince >= kPairingPendingCredentialTtl) {
    pendingCredential_.reset();
  }
}

void PairingSession::ExpireChallengeIfElapsedLocked() {
  if (activeChallenge_.has_value() && !activeChallenge_->IsAvailable()) {
    ClearChallengeLocked();
  }
}

void PairingSession::ClearChallengeLocked() {
  activeChallenge_.reset();
  activeCode_.reset();
  ownerClientId_.reset();
  disconnectedAt_.reset();
  wrongAttempts_ = 0;
  lastConfirmAttemptAt_.reset();
  renotifyCooldownUntil_.reset();
  autoRenotifyCooldownUntil_.reset();
}

StartChallengeResult
PairingSession::TryStartChallenge(const std::string &clientId,
                                  std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  ExpirePendingIfElapsedLocked(now);

  if (pendingCredential_.has_value()) {
    if (pendingCredential_->clientId == clientId) {
      return {.outcome = StartChallengeOutcome::kResumed, .code = std::nullopt};
    }
    return {.outcome = StartChallengeOutcome::kOtherDeviceActive,
            .code = std::nullopt};
  }
  if (activeChallenge_.has_value() && activeChallenge_->IsAvailable()) {
    if (ownerClientId_.has_value() && *ownerClientId_ == clientId) {
      return {.outcome = StartChallengeOutcome::kResumed, .code = std::nullopt};
    }
    return {.outcome = StartChallengeOutcome::kOtherDeviceActive,
            .code = std::nullopt};
  }

  auto code = codeGenerator_();
  if (!code.has_value()) {
    return {.outcome = StartChallengeOutcome::kGeneratorFailed,
            .code = std::nullopt};
  }

  // Clears any stale owner/attempt/cooldown bookkeeping left behind by a
  // challenge that expired naturally (via TokenStore's own TTL, which the
  // branches above don't otherwise clean up).
  ClearChallengeLocked();
  std::vector<std::uint8_t> codeBytes(code->begin(), code->end());
  activeChallenge_.emplace(std::move(codeBytes), codeTimeToLive_);
  activeCode_ = code;
  ownerClientId_ = clientId;
  return {.outcome = StartChallengeOutcome::kStarted, .code = code};
}

std::optional<std::chrono::seconds>
PairingSession::RemainingSeconds(std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  if (!activeChallenge_.has_value()) {
    return std::nullopt;
  }
  return activeChallenge_->RemainingSeconds();
}

std::optional<std::string>
PairingSession::CurrentCode(std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  ExpireChallengeIfElapsedLocked();
  if (!activeChallenge_.has_value()) {
    return std::nullopt;
  }
  return activeCode_;
}

void PairingSession::NotifyDisconnected(
    const std::string &clientId, std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  if (activeChallenge_.has_value() && ownerClientId_.has_value() &&
      *ownerClientId_ == clientId) {
    disconnectedAt_ = now;
  }
}

void PairingSession::NotifyReconnected(
    const std::string &clientId, std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  if (ownerClientId_.has_value() && *ownerClientId_ == clientId) {
    disconnectedAt_.reset();
  }
}

ConfirmCodeResult PairingSession::TryConfirmCode(
    const std::string &presentedCode, std::chrono::steady_clock::time_point now,
    std::string clientId, std::vector<std::uint8_t> credential,
    std::optional<std::string> displayName) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  if (!activeChallenge_.has_value()) {
    return {.outcome = ConfirmResult::kInvalid, .shouldAutoRenotify = false};
  }
  // Indistinguishable from a wrong code to a non-owner: reveals nothing about
  // the challenge or its real owner, and never even reaches the
  // expiry/pacing/evaluation logic below.
  if (ownerClientId_.has_value() && *ownerClientId_ != clientId) {
    return {.outcome = ConfirmResult::kInvalid, .shouldAutoRenotify = false};
  }
  // Checked ahead of pacing/TryReserve (which would themselves fail identically
  // either way) so a caller can be told "expired" instead of the less useful
  // "invalid" or "pacing_limited".
  if (!activeChallenge_->IsAvailable()) {
    return {.outcome = ConfirmResult::kExpired, .shouldAutoRenotify = false};
  }
  if (lastConfirmAttemptAt_.has_value() &&
      now - *lastConfirmAttemptAt_ < kPairingConfirmPacingInterval) {
    auto remaining = std::chrono::ceil<std::chrono::seconds>(
        kPairingConfirmPacingInterval - (now - *lastConfirmAttemptAt_));
    return {.outcome = ConfirmResult::kPacingLimited,
            .shouldAutoRenotify = false,
            .retryAfterSeconds = remaining};
  }
  lastConfirmAttemptAt_ = now;

  std::vector<std::uint8_t> presentedBytes(presentedCode.begin(),
                                           presentedCode.end());
  auto reservation = activeChallenge_->TryReserve(presentedBytes);
  if (!reservation.has_value()) {
    ++wrongAttempts_;
    if (wrongAttempts_ >= kPairingMaxWrongAttempts) {
      ClearChallengeLocked();
      return {.outcome = ConfirmResult::kHardLimitReached,
              .shouldAutoRenotify = false};
    }
    bool shouldAutoRenotify = !autoRenotifyCooldownUntil_.has_value() ||
                              now >= *autoRenotifyCooldownUntil_;
    if (shouldAutoRenotify) {
      autoRenotifyCooldownUntil_ = now + kPairingRenotifyCooldown;
    }
    return {.outcome = ConfirmResult::kInvalid,
            .shouldAutoRenotify = shouldAutoRenotify};
  }

  reservation->Commit();
  pendingCredential_ = PendingCredential{
      .clientId = std::move(clientId),
      .credential = std::move(credential),
      .displayName = std::move(displayName),
      .pendingSince = now,
  };
  ClearChallengeLocked();
  return {.outcome = ConfirmResult::kConfirmed, .shouldAutoRenotify = false};
}

std::optional<PendingCredential>
PairingSession::PeekPending(const std::string &clientId,
                            const std::vector<std::uint8_t> &credential,
                            std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpirePendingIfElapsedLocked(now);
  if (!pendingCredential_.has_value()) {
    return std::nullopt;
  }
  if (pendingCredential_->clientId != clientId ||
      !ConstantTimeEquals(pendingCredential_->credential, credential)) {
    return std::nullopt;
  }
  return pendingCredential_;
}

bool PairingSession::CommitPending(const std::string &clientId,
                                   const std::vector<std::uint8_t> &credential,
                                   std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpirePendingIfElapsedLocked(now);
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

RenotifyResult
PairingSession::TryRenotify(const std::string &clientId,
                            std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  ExpireChallengeIfElapsedLocked();
  if (!activeChallenge_.has_value() || !ownerClientId_.has_value() ||
      *ownerClientId_ != clientId) {
    return {.outcome = RenotifyOutcome::kNotActive,
            .retryAfterSeconds = std::nullopt};
  }
  if (renotifyCooldownUntil_.has_value() && now < *renotifyCooldownUntil_) {
    auto remaining =
        std::chrono::ceil<std::chrono::seconds>(*renotifyCooldownUntil_ - now);
    return {.outcome = RenotifyOutcome::kCooldown,
            .retryAfterSeconds = remaining};
  }
  renotifyCooldownUntil_ = now + kPairingRenotifyCooldown;
  return {.outcome = RenotifyOutcome::kRenotified,
          .retryAfterSeconds = std::nullopt};
}

CancelOutcome
PairingSession::TryCancel(const std::string &clientId,
                          std::chrono::steady_clock::time_point now) {
  std::lock_guard<std::mutex> lock(mutex_);
  ExpireOwnerIfGraceElapsedLocked(now);
  ExpirePendingIfElapsedLocked(now);
  ExpireChallengeIfElapsedLocked();

  if (pendingCredential_.has_value() &&
      pendingCredential_->clientId == clientId) {
    pendingCredential_.reset();
    return CancelOutcome::kCancelled;
  }
  if (activeChallenge_.has_value() && ownerClientId_.has_value() &&
      *ownerClientId_ == clientId) {
    ClearChallengeLocked();
    return CancelOutcome::kCancelled;
  }
  return CancelOutcome::kAlreadyIdle;
}

void PairingSession::CancelAll() {
  std::lock_guard<std::mutex> lock(mutex_);
  ClearChallengeLocked();
  pendingCredential_.reset();
}

} // namespace dovahlink::security

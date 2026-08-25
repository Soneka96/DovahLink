#include "security/trust_store.hpp"

#include "security/constant_time_compare.hpp"
#include "security/csprng.hpp"
#include "security/limits.hpp"

#include <windows.h>

#include <cctype>
#include <chrono>
#include <string_view>
#include <unordered_map>
#include <utility>

namespace dovahlink::security {

namespace {

/// Overwrites a byte buffer without performing a potentially throwing
/// reallocation.
void SecureClear(std::vector<std::uint8_t> &buffer) noexcept {
  if (!buffer.empty()) {
    SecureZeroMemory(buffer.data(), buffer.size());
  }
  buffer.clear();
}

/// Overwrites every device's credential in a whole-map rollback copy that a
/// successful mutation no longer needs. A copy taken only to restore `devices_`
/// on a `Save` failure still holds whatever credential bytes the live map held
/// at that moment; once the mutation succeeds and the copy is discarded
/// instead, those bytes must not linger un-zeroed in memory until the copy's
/// backing allocation happens to be reused.
void SecureClearDevices(
    std::unordered_map<std::string, KnownDeviceRecord> &devices) noexcept {
  for (auto &[clientId, device] : devices) {
    SecureClear(device.credential);
  }
}

} // namespace

TrustStore::TrustStore(ITrustStorePersistence &persistence,
                       ShortIdGenerator shortIdGenerator,
                       TrustStoreSnapshot snapshot, bool wasCorruptOnLoad)
    : persistence_(&persistence),
      shortIdGenerator_(std::move(shortIdGenerator)),
      wasCorruptOnLoad_(wasCorruptOnLoad) {
  for (auto &device : snapshot.devices) {
    auto clientId = device.clientId;
    devices_.emplace(std::move(clientId), std::move(device));
  }
}

TrustStore TrustStore::Load(ITrustStorePersistence &persistence,
                            ShortIdGenerator shortIdGenerator) {
  auto snapshot = persistence.Load();
  if (!snapshot.has_value()) {
    return TrustStore(persistence, std::move(shortIdGenerator),
                      TrustStoreSnapshot{},
                      /*wasCorruptOnLoad=*/true);
  }
  return TrustStore(persistence, std::move(shortIdGenerator),
                    std::move(*snapshot),
                    /*wasCorruptOnLoad=*/false);
}

std::optional<std::string> TrustStore::DefaultShortIdGenerator() {
  constexpr std::size_t kShortIdDigits = 5;
  return GenerateNumericCode(kShortIdDigits);
}

bool TrustStore::WasCorruptOnLoad() const noexcept { return wasCorruptOnLoad_; }

std::optional<KnownDeviceRecord>
TrustStore::Query(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end() || it->second.state != KnownDeviceState::kTrusted) {
    return std::nullopt;
  }
  return it->second;
}

std::vector<KnownDeviceRecord> TrustStore::ListTrusted() {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<KnownDeviceRecord> result;
  for (const auto &[clientId, device] : devices_) {
    if (device.state == KnownDeviceState::kTrusted) {
      result.push_back(device);
    }
  }
  return result;
}

std::vector<KnownDeviceRecord> TrustStore::ListAll() {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<KnownDeviceRecord> result;
  result.reserve(devices_.size());
  for (const auto &[clientId, device] : devices_) {
    result.push_back(device);
  }
  return result;
}

bool TrustStore::IsRevoked(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  return it != devices_.end() && it->second.state == KnownDeviceState::kRevoked;
}

bool TrustStore::IsBlocked(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  return it != devices_.end() && it->second.state == KnownDeviceState::kBlocked;
}

std::optional<KnownDeviceRecord>
TrustStore::FindByShortId(std::string_view shortId) {
  std::lock_guard<std::mutex> lock(mutex_);
  for (const auto &[clientId, device] : devices_) {
    if (device.shortId == shortId) {
      return device;
    }
  }
  return std::nullopt;
}

bool TrustStore::Authenticate(
    const std::string &clientId,
    const std::vector<std::uint8_t> &presentedCredential) {
  if (presentedCredential.empty()) {
    return false;
  }
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end() || it->second.state != KnownDeviceState::kTrusted) {
    return false;
  }
  return ConstantTimeEquals(it->second.credential, presentedCredential);
}

bool TrustStore::IsValidDisplayName(const std::string &displayName) {
  if (displayName.size() > kMaxDisplayNameLengthBytes) {
    return false;
  }
  for (unsigned char ch : displayName) {
    if (std::iscntrl(ch)) {
      return false;
    }
  }
  return true;
}

std::optional<std::string> TrustStore::GenerateUniqueShortId() {
  for (std::size_t attempt = 0; attempt < kMaxShortIdGenerationAttempts;
       ++attempt) {
    auto candidate = shortIdGenerator_();
    if (!candidate.has_value()) {
      return std::nullopt;
    }
    bool collides = false;
    for (const auto &[clientId, device] : devices_) {
      if (device.shortId == *candidate) {
        collides = true;
        break;
      }
    }
    if (!collides) {
      return candidate;
    }
  }
  return std::nullopt;
}

TrustStoreSnapshot TrustStore::BuildSnapshot() const {
  TrustStoreSnapshot snapshot;
  snapshot.devices.reserve(devices_.size());
  for (const auto &[clientId, device] : devices_) {
    snapshot.devices.push_back(device);
  }
  return snapshot;
}

std::optional<KnownDeviceRecord>
TrustStore::Persist(std::string clientId, std::vector<std::uint8_t> credential,
                    std::optional<std::string> displayName) {
  std::lock_guard<std::mutex> lock(mutex_);
  if (clientId.empty() || credential.empty()) {
    return std::nullopt;
  }
  if (displayName.has_value() && !IsValidDisplayName(*displayName)) {
    return std::nullopt;
  }

  // A known device (in any prior state) keeps its shortId and createdAt for the
  // lifetime of its record; only a genuinely new clientId mints fresh ones. A
  // kBlocked device is the one prior state that must not be able to re-pair
  // straight to kTrusted -- it requires an explicit Unblock first, per
  // enums.hpp's KnownDeviceState::kBlocked contract.
  auto existing = devices_.find(clientId);
  if (existing != devices_.end() &&
      existing->second.state == KnownDeviceState::kBlocked) {
    return std::nullopt;
  }
  std::string shortId;
  std::chrono::system_clock::time_point createdAt;
  if (existing != devices_.end()) {
    shortId = existing->second.shortId;
    createdAt = existing->second.createdAt;
    // An omitted displayName (std::nullopt) preserves whatever the record
    // already held on re-pair; a supplied value -- including an explicit empty
    // string -- always replaces it.
    if (!displayName.has_value()) {
      displayName = existing->second.displayName;
    }
  } else {
    auto generated = GenerateUniqueShortId();
    if (!generated.has_value()) {
      return std::nullopt;
    }
    shortId = std::move(*generated);
    createdAt = std::chrono::system_clock::now();
  }

  // An explicit empty string clears the name -- stored as std::nullopt, never
  // an empty string, so "no display name" stays one canonical representation,
  // matching Rename's own normalization.
  if (displayName.has_value() && displayName->empty()) {
    displayName.reset();
  }

  KnownDeviceRecord device{.clientId = clientId,
                           .credential = std::move(credential),
                           .shortId = std::move(shortId),
                           .displayName = std::move(displayName),
                           .state = KnownDeviceState::kTrusted,
                           .createdAt = createdAt};

  auto previousDevices = devices_;
  auto it = devices_.insert_or_assign(clientId, std::move(device)).first;

  if (!persistence_->Save(BuildSnapshot())) {
    devices_ = std::move(previousDevices);
    return std::nullopt;
  }
  SecureClearDevices(previousDevices);
  return it->second;
}

bool TrustStore::Revoke(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end() || it->second.state != KnownDeviceState::kTrusted) {
    // Unknown, or already not trusted: nothing to change. Only a clientId that
    // was actually trusted transitions to a durable Revoked record (see
    // IsRevoked).
    return true;
  }

  auto previousDevice = it->second;
  it->second.state = KnownDeviceState::kRevoked;
  SecureClear(it->second.credential);

  if (!persistence_->Save(BuildSnapshot())) {
    it->second = std::move(previousDevice);
    return false;
  }
  SecureClear(previousDevice.credential);
  return true;
}

BlockOutcome TrustStore::Block(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end()) {
    return BlockOutcome::kNotFound;
  }
  if (it->second.state == KnownDeviceState::kBlocked) {
    return BlockOutcome::kAlreadyBlocked;
  }
  if (it->second.state != KnownDeviceState::kTrusted &&
      it->second.state != KnownDeviceState::kRevoked) {
    // Only kUnpaired remains, per the four-state model; not eligible for
    // blocking in this phase.
    return BlockOutcome::kNotEligible;
  }

  auto previousDevice = it->second;
  it->second.state = KnownDeviceState::kBlocked;
  it->second.blockedAt = std::chrono::system_clock::now();
  SecureClear(it->second.credential);

  if (!persistence_->Save(BuildSnapshot())) {
    it->second = std::move(previousDevice);
    return BlockOutcome::kSaveFailed;
  }
  SecureClear(previousDevice.credential);
  return BlockOutcome::kBlocked;
}

UnblockOutcome TrustStore::Unblock(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end()) {
    return UnblockOutcome::kNotFound;
  }
  if (it->second.state != KnownDeviceState::kBlocked) {
    return UnblockOutcome::kNotBlocked;
  }

  auto previousDevice = it->second;
  it->second.state = KnownDeviceState::kUnpaired;
  it->second.blockedAt.reset();

  if (!persistence_->Save(BuildSnapshot())) {
    it->second = std::move(previousDevice);
    return UnblockOutcome::kSaveFailed;
  }
  return UnblockOutcome::kUnblocked;
}

bool TrustStore::Reset() {
  std::lock_guard<std::mutex> lock(mutex_);
  auto previousDevices = std::move(devices_);
  devices_.clear();

  if (!persistence_->Save(BuildSnapshot())) {
    devices_ = std::move(previousDevices);
    return false;
  }
  for (auto &[clientId, device] : previousDevices) {
    SecureClear(device.credential);
  }
  return true;
}

ForgetOutcome TrustStore::Forget(const std::string &clientId) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end()) {
    return ForgetOutcome::kNotFound;
  }
  if (it->second.state != KnownDeviceState::kRevoked &&
      it->second.state != KnownDeviceState::kUnpaired) {
    return ForgetOutcome::kNotEligible;
  }

  auto previousDevices = devices_;
  devices_.erase(it);

  if (!persistence_->Save(BuildSnapshot())) {
    devices_ = std::move(previousDevices);
    return ForgetOutcome::kSaveFailed;
  }
  SecureClearDevices(previousDevices);
  return ForgetOutcome::kForgotten;
}

RenameOutcome TrustStore::Rename(const std::string &clientId,
                                 std::string displayName) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = devices_.find(clientId);
  if (it == devices_.end()) {
    return RenameOutcome::kNotFound;
  }
  if (it->second.state != KnownDeviceState::kTrusted) {
    return RenameOutcome::kNotEligible;
  }
  if (!displayName.empty() && !IsValidDisplayName(displayName)) {
    return RenameOutcome::kInvalidDisplayName;
  }

  auto previousDevice = it->second;
  it->second.displayName = displayName.empty()
                               ? std::nullopt
                               : std::optional(std::move(displayName));

  if (!persistence_->Save(BuildSnapshot())) {
    it->second = std::move(previousDevice);
    return RenameOutcome::kSaveFailed;
  }
  return RenameOutcome::kRenamed;
}

bool TrustStore::ResetTrust() {
  std::lock_guard<std::mutex> lock(mutex_);
  auto previousDevices = devices_;
  for (auto &[clientId, device] : devices_) {
    if (device.state == KnownDeviceState::kTrusted) {
      device.state = KnownDeviceState::kRevoked;
      SecureClear(device.credential);
    }
  }

  if (!persistence_->Save(BuildSnapshot())) {
    devices_ = std::move(previousDevices);
    return false;
  }
  SecureClearDevices(previousDevices);
  return true;
}

} // namespace dovahlink::security

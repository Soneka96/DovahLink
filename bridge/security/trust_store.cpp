#include "security/trust_store.hpp"

#include "security/constant_time_compare.hpp"
#include "security/csprng.hpp"
#include "security/limits.hpp"

#include <windows.h>

#include <cctype>
#include <chrono>
#include <utility>

namespace dovahlink::security {

namespace {

/// Overwrites a byte buffer without performing a potentially throwing reallocation.
void SecureClear(std::vector<std::uint8_t>& buffer) noexcept {
    if (!buffer.empty()) {
        SecureZeroMemory(buffer.data(), buffer.size());
    }
    buffer.clear();
}

}  // namespace

TrustStore::TrustStore(ITrustStorePersistence& persistence, ShortIdGenerator shortIdGenerator,
                        TrustStoreSnapshot snapshot, bool wasCorruptOnLoad)
    : persistence_(&persistence),
      shortIdGenerator_(std::move(shortIdGenerator)),
      wasCorruptOnLoad_(wasCorruptOnLoad) {
    for (auto& device : snapshot.devices) {
        auto clientId = device.clientId;
        devices_.emplace(std::move(clientId), std::move(device));
    }
}

TrustStore TrustStore::Load(ITrustStorePersistence& persistence,
                             ShortIdGenerator shortIdGenerator) {
    auto snapshot = persistence.Load();
    if (!snapshot.has_value()) {
        return TrustStore(persistence, std::move(shortIdGenerator), TrustStoreSnapshot{},
                           /*wasCorruptOnLoad=*/true);
    }
    return TrustStore(persistence, std::move(shortIdGenerator), std::move(*snapshot),
                       /*wasCorruptOnLoad=*/false);
}

std::optional<std::string> TrustStore::DefaultShortIdGenerator() {
    constexpr std::size_t kShortIdDigits = 5;
    return GenerateNumericCode(kShortIdDigits);
}

bool TrustStore::WasCorruptOnLoad() const noexcept { return wasCorruptOnLoad_; }

std::optional<KnownDeviceRecord> TrustStore::Query(const std::string& clientId) {
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
    for (const auto& [clientId, device] : devices_) {
        if (device.state == KnownDeviceState::kTrusted) {
            result.push_back(device);
        }
    }
    return result;
}

bool TrustStore::IsRevoked(const std::string& clientId) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = devices_.find(clientId);
    return it != devices_.end() && it->second.state == KnownDeviceState::kRevoked;
}

bool TrustStore::Authenticate(const std::string& clientId,
                               const std::vector<std::uint8_t>& presentedCredential) {
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

bool TrustStore::IsValidDisplayName(const std::string& displayName) {
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
    for (std::size_t attempt = 0; attempt < kMaxShortIdGenerationAttempts; ++attempt) {
        auto candidate = shortIdGenerator_();
        if (!candidate.has_value()) {
            return std::nullopt;
        }
        bool collides = false;
        for (const auto& [clientId, device] : devices_) {
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
    for (const auto& [clientId, device] : devices_) {
        snapshot.devices.push_back(device);
    }
    return snapshot;
}

std::optional<KnownDeviceRecord> TrustStore::Persist(std::string clientId,
                                                      std::vector<std::uint8_t> credential,
                                                      std::optional<std::string> displayName) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (clientId.empty() || credential.empty()) {
        return std::nullopt;
    }
    if (displayName.has_value() && !IsValidDisplayName(*displayName)) {
        return std::nullopt;
    }

    // A known device (in any prior state) keeps its shortId and createdAt for the lifetime of its
    // record; only a genuinely new clientId mints fresh ones.
    auto existing = devices_.find(clientId);
    std::string shortId;
    std::chrono::system_clock::time_point createdAt;
    if (existing != devices_.end()) {
        shortId = existing->second.shortId;
        createdAt = existing->second.createdAt;
    } else {
        auto generated = GenerateUniqueShortId();
        if (!generated.has_value()) {
            return std::nullopt;
        }
        shortId = std::move(*generated);
        createdAt = std::chrono::system_clock::now();
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
    return it->second;
}

bool TrustStore::Revoke(const std::string& clientId) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = devices_.find(clientId);
    if (it == devices_.end() || it->second.state != KnownDeviceState::kTrusted) {
        // Unknown, or already not trusted: nothing to change. Only a clientId that was actually
        // trusted transitions to a durable Revoked record (see IsRevoked).
        return true;
    }

    auto previousDevice = it->second;
    it->second.state = KnownDeviceState::kRevoked;
    SecureClear(it->second.credential);

    if (!persistence_->Save(BuildSnapshot())) {
        it->second = std::move(previousDevice);
        return false;
    }
    return true;
}

bool TrustStore::Reset() {
    std::lock_guard<std::mutex> lock(mutex_);
    auto previousDevices = std::move(devices_);
    devices_.clear();

    if (!persistence_->Save(BuildSnapshot())) {
        devices_ = std::move(previousDevices);
        return false;
    }
    for (auto& [clientId, device] : previousDevices) {
        SecureClear(device.credential);
    }
    return true;
}

}  // namespace dovahlink::security

#include "security/trust_store.hpp"

#include "security/csprng.hpp"
#include "security/limits.hpp"

#include <windows.h>

#include <cctype>
#include <format>
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
    for (auto& record : snapshot.records) {
        auto clientId = record.clientId;
        records_.emplace(std::move(clientId), std::move(record));
    }
    for (auto& tombstone : snapshot.tombstones) {
        tombstones_.insert(std::move(tombstone.clientId));
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
    constexpr std::uint32_t kShortIdRange = 100'000;
    auto bytes = GenerateRandomBytes(sizeof(std::uint32_t));
    if (!bytes.has_value()) {
        return std::nullopt;
    }
    std::uint32_t value = 0;
    for (auto byte : *bytes) {
        value = (value << 8) | byte;
    }
    return std::format("{:05d}", value % kShortIdRange);
}

bool TrustStore::WasCorruptOnLoad() const noexcept { return wasCorruptOnLoad_; }

std::optional<TrustedClientRecord> TrustStore::Query(const std::string& clientId) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = records_.find(clientId);
    if (it == records_.end()) {
        return std::nullopt;
    }
    return it->second;
}

std::vector<TrustedClientRecord> TrustStore::ListTrusted() {
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<TrustedClientRecord> result;
    result.reserve(records_.size());
    for (const auto& [clientId, record] : records_) {
        result.push_back(record);
    }
    return result;
}

bool TrustStore::IsRevoked(const std::string& clientId) {
    std::lock_guard<std::mutex> lock(mutex_);
    return tombstones_.contains(clientId);
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
        for (const auto& [clientId, record] : records_) {
            if (record.shortId == *candidate) {
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
    snapshot.records.reserve(records_.size());
    for (const auto& [clientId, record] : records_) {
        snapshot.records.push_back(record);
    }
    snapshot.tombstones.reserve(tombstones_.size());
    for (const auto& clientId : tombstones_) {
        snapshot.tombstones.push_back(RevocationTombstone{.clientId = clientId});
    }
    return snapshot;
}

std::optional<TrustedClientRecord> TrustStore::Persist(std::string clientId,
                                                        std::vector<std::uint8_t> credential,
                                                        std::optional<std::string> displayName) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (clientId.empty() || credential.empty()) {
        return std::nullopt;
    }
    if (displayName.has_value() && !IsValidDisplayName(*displayName)) {
        return std::nullopt;
    }
    auto shortId = GenerateUniqueShortId();
    if (!shortId.has_value()) {
        return std::nullopt;
    }

    TrustedClientRecord record{.clientId = clientId,
                                .credential = std::move(credential),
                                .shortId = std::move(*shortId),
                                .displayName = std::move(displayName)};

    auto previousRecords = records_;
    auto previousTombstones = tombstones_;
    tombstones_.erase(clientId);
    auto it = records_.insert_or_assign(clientId, std::move(record)).first;

    if (!persistence_->Save(BuildSnapshot())) {
        records_ = std::move(previousRecords);
        tombstones_ = std::move(previousTombstones);
        return std::nullopt;
    }
    return it->second;
}

bool TrustStore::Revoke(const std::string& clientId) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = records_.find(clientId);
    if (it == records_.end()) {
        // Never trusted, so nothing distinguishes it from any other unknown string; only a
        // clientId that was actually trusted earns a tombstone (see IsRevoked).
        return true;
    }

    auto removed = std::move(it->second);
    records_.erase(it);
    tombstones_.insert(clientId);

    if (!persistence_->Save(BuildSnapshot())) {
        records_.emplace(clientId, std::move(removed));
        tombstones_.erase(clientId);
        return false;
    }
    SecureClear(removed.credential);
    return true;
}

bool TrustStore::Reset() {
    std::lock_guard<std::mutex> lock(mutex_);
    auto previousRecords = std::move(records_);
    auto previousTombstones = std::move(tombstones_);
    records_.clear();
    tombstones_.clear();

    if (!persistence_->Save(BuildSnapshot())) {
        records_ = std::move(previousRecords);
        tombstones_ = std::move(previousTombstones);
        return false;
    }
    for (auto& [clientId, record] : previousRecords) {
        SecureClear(record.credential);
    }
    return true;
}

}  // namespace dovahlink::security

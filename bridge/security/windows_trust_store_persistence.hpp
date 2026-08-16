#pragma once

#include "security/trust_store.hpp"

#include <filesystem>
#include <optional>

namespace dovahlink::security {

/// `ITrustStorePersistence` backed by one DPAPI-encrypted file on disk. The `TrustStore` domain
/// object has no knowledge of this class, DPAPI, or the file format -- see `dpapi.hpp` and
/// `ai/context/protocol/security.md`'s "Persistent local trust" section for the approved design
/// this implements.
class WindowsTrustStorePersistence : public ITrustStorePersistence {
public:
    /// Targets `path` for load and save. The path's parent directory need not exist yet; `Save`
    /// creates it. `path` does not need to exist for `Load` to succeed -- a missing file loads as
    /// a valid empty snapshot.
    explicit WindowsTrustStorePersistence(std::filesystem::path path);

    /// Loads the snapshot from `path`, or `std::nullopt` when the file exists but cannot be read,
    /// decrypted, or parsed into a valid snapshot shape. A missing file is a successful load of an
    /// empty snapshot, not a corrupt one.
    [[nodiscard]] std::optional<TrustStoreSnapshot> Load() override;

    /// Serializes `snapshot` to JSON, encrypts it for the current Windows user, and atomically
    /// replaces `path` with the result so a crash mid-write cannot leave a partially written file.
    /// @return Whether every step (serialize, encrypt, write, replace) succeeded.
    [[nodiscard]] bool Save(const TrustStoreSnapshot& snapshot) override;

private:
    /// File this instance loads from and saves to.
    std::filesystem::path path_;
};

/// Resolves the real Windows per-user path `WindowsTrustStorePersistence` uses in production,
/// under the current user's local application-data directory. Returns `std::nullopt` when that
/// directory cannot be determined (`LOCALAPPDATA` unset). Not used by this class's own tests,
/// which inject an explicit path instead.
[[nodiscard]] std::optional<std::filesystem::path> ResolveDefaultTrustStorePath();

}  // namespace dovahlink::security

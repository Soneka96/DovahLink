#include "security/windows_trust_store_persistence.hpp"

#include "security/dpapi.hpp"
#include "security/hex.hpp"

#include <windows.h>

#include <boost/json/array.hpp>
#include <boost/json/object.hpp>
#include <boost/json/parse.hpp>
#include <boost/json/serialize.hpp>
#include <boost/json/value.hpp>
#include <boost/system/error_code.hpp>

#include <fstream>
#include <iterator>
#include <string>
#include <system_error>
#include <utility>

namespace dovahlink::security {

namespace {

/// Encodes a snapshot as JSON. `credential` is hex-encoded (JSON has no binary type); `shortId`
/// and `clientId` are already text. `displayName` is a string or JSON `null`.
std::string EncodeSnapshotToJson(const TrustStoreSnapshot& snapshot) {
    boost::json::array records;
    for (const auto& record : snapshot.records) {
        boost::json::object obj;
        obj["clientId"] = record.clientId;
        obj["credential"] = EncodeHex(record.credential);
        obj["shortId"] = record.shortId;
        obj["displayName"] =
            record.displayName.has_value() ? boost::json::value(*record.displayName) : boost::json::value(nullptr);
        records.push_back(std::move(obj));
    }

    boost::json::array tombstones;
    for (const auto& tombstone : snapshot.tombstones) {
        boost::json::object obj;
        obj["clientId"] = tombstone.clientId;
        tombstones.push_back(std::move(obj));
    }

    boost::json::object root;
    root["records"] = std::move(records);
    root["tombstones"] = std::move(tombstones);
    return boost::json::serialize(root);
}

/// Decodes one record from its JSON object form, or `std::nullopt` for any shape mismatch.
/// Content trustworthiness (e.g. that `displayName` still satisfies its bound) is guaranteed by
/// DPAPI's authenticated decryption having already succeeded on this exact file, not re-checked
/// here -- this only validates JSON structure and types.
std::optional<TrustedClientRecord> DecodeRecord(const boost::json::value& item) {
    if (!item.is_object()) {
        return std::nullopt;
    }
    const auto& obj = item.get_object();
    const boost::json::value* clientIdValue = obj.if_contains("clientId");
    const boost::json::value* credentialValue = obj.if_contains("credential");
    const boost::json::value* shortIdValue = obj.if_contains("shortId");
    const boost::json::value* displayNameValue = obj.if_contains("displayName");
    if (!clientIdValue || !clientIdValue->is_string() || !credentialValue || !credentialValue->is_string() ||
        !shortIdValue || !shortIdValue->is_string() || !displayNameValue) {
        return std::nullopt;
    }

    auto credential = DecodeHex(credentialValue->get_string());
    if (!credential.has_value()) {
        return std::nullopt;
    }

    std::optional<std::string> displayName;
    if (displayNameValue->is_string()) {
        displayName = std::string(displayNameValue->get_string());
    } else if (!displayNameValue->is_null()) {
        return std::nullopt;
    }

    return TrustedClientRecord{
        .clientId = std::string(clientIdValue->get_string()),
        .credential = std::move(*credential),
        .shortId = std::string(shortIdValue->get_string()),
        .displayName = std::move(displayName),
    };
}

/// Decodes one tombstone from its JSON object form, or `std::nullopt` for any shape mismatch.
std::optional<RevocationTombstone> DecodeTombstone(const boost::json::value& item) {
    if (!item.is_object()) {
        return std::nullopt;
    }
    const boost::json::value* clientIdValue = item.get_object().if_contains("clientId");
    if (!clientIdValue || !clientIdValue->is_string()) {
        return std::nullopt;
    }
    return RevocationTombstone{.clientId = std::string(clientIdValue->get_string())};
}

/// Decodes a full snapshot from JSON text, or `std::nullopt` for any structural mismatch.
std::optional<TrustStoreSnapshot> DecodeSnapshotFromJson(std::string_view text) {
    boost::system::error_code ec;
    boost::json::value parsed = boost::json::parse(text, ec);
    if (ec || !parsed.is_object()) {
        return std::nullopt;
    }
    const boost::json::object& root = parsed.get_object();

    const boost::json::value* recordsValue = root.if_contains("records");
    const boost::json::value* tombstonesValue = root.if_contains("tombstones");
    if (!recordsValue || !recordsValue->is_array() || !tombstonesValue || !tombstonesValue->is_array()) {
        return std::nullopt;
    }

    TrustStoreSnapshot snapshot;
    for (const auto& item : recordsValue->get_array()) {
        auto record = DecodeRecord(item);
        if (!record.has_value()) {
            return std::nullopt;
        }
        snapshot.records.push_back(std::move(*record));
    }
    for (const auto& item : tombstonesValue->get_array()) {
        auto tombstone = DecodeTombstone(item);
        if (!tombstone.has_value()) {
            return std::nullopt;
        }
        snapshot.tombstones.push_back(std::move(*tombstone));
    }
    return snapshot;
}

/// Reads the complete contents of `path`, or `std::nullopt` when it cannot be opened or read.
std::optional<std::vector<std::uint8_t>> ReadFileBytes(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) {
        return std::nullopt;
    }
    std::vector<std::uint8_t> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    if (file.bad()) {
        return std::nullopt;
    }
    return bytes;
}

/// Overwrites `path` with `bytes`, truncating any existing content.
/// @return Whether the write succeeded.
bool WriteFileBytes(const std::filesystem::path& path, const std::vector<std::uint8_t>& bytes) {
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    if (!file.is_open()) {
        return false;
    }
    file.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    return file.good();
}

}  // namespace

WindowsTrustStorePersistence::WindowsTrustStorePersistence(std::filesystem::path path)
    : path_(std::move(path)) {}

std::optional<TrustStoreSnapshot> WindowsTrustStorePersistence::Load() {
    std::error_code existsEc;
    bool fileExists = std::filesystem::exists(path_, existsEc);
    if (existsEc) {
        return std::nullopt;
    }
    if (!fileExists) {
        return TrustStoreSnapshot{};
    }

    auto encrypted = ReadFileBytes(path_);
    if (!encrypted.has_value()) {
        return std::nullopt;
    }
    auto decrypted = DecryptForCurrentUser(*encrypted);
    if (!decrypted.has_value()) {
        return std::nullopt;
    }
    return DecodeSnapshotFromJson(std::string(decrypted->begin(), decrypted->end()));
}

bool WindowsTrustStorePersistence::Save(const TrustStoreSnapshot& snapshot) {
    auto json = EncodeSnapshotToJson(snapshot);
    std::vector<std::uint8_t> plaintext(json.begin(), json.end());

    auto encrypted = EncryptForCurrentUser(plaintext);
    if (!encrypted.has_value()) {
        return false;
    }

    if (!path_.parent_path().empty()) {
        std::error_code dirEc;
        std::filesystem::create_directories(path_.parent_path(), dirEc);
        if (dirEc) {
            return false;
        }
    }

    std::filesystem::path tempPath = path_;
    tempPath += L".tmp";
    if (!WriteFileBytes(tempPath, *encrypted)) {
        return false;
    }

    if (!MoveFileExW(tempPath.c_str(), path_.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        std::error_code cleanupEc;
        std::filesystem::remove(tempPath, cleanupEc);
        return false;
    }
    return true;
}

std::optional<std::filesystem::path> ResolveDefaultTrustStorePath() {
    constexpr DWORD kMaxEnvValueChars = 4096;
    std::wstring buffer(kMaxEnvValueChars, L'\0');
    DWORD written = GetEnvironmentVariableW(L"LOCALAPPDATA", buffer.data(), kMaxEnvValueChars);
    if (written == 0 || written >= kMaxEnvValueChars) {
        return std::nullopt;
    }
    buffer.resize(written);

    std::filesystem::path path(buffer);
    path /= L"DovahLink";
    path /= L"trust_store.dat";
    return path;
}

}  // namespace dovahlink::security

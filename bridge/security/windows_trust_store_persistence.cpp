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

#include <chrono>
#include <cstdint>
#include <fstream>
#include <iterator>
#include <string>
#include <system_error>
#include <utility>

namespace dovahlink::security {

namespace {

/// Encodes `state` as its persisted lowercase string form.
std::string EncodeState(KnownDeviceState state) {
  switch (state) {
  case KnownDeviceState::kTrusted:
    return "trusted";
  case KnownDeviceState::kRevoked:
    return "revoked";
  case KnownDeviceState::kBlocked:
    return "blocked";
  case KnownDeviceState::kUnpaired:
    return "unpaired";
  }
  // Unreachable: every enumerator is handled above.
  return "trusted";
}

/// Decodes a persisted state string back to its enumerator, or `std::nullopt`
/// for any other text.
std::optional<KnownDeviceState> DecodeState(std::string_view text) {
  if (text == "trusted") {
    return KnownDeviceState::kTrusted;
  }
  if (text == "revoked") {
    return KnownDeviceState::kRevoked;
  }
  if (text == "blocked") {
    return KnownDeviceState::kBlocked;
  }
  if (text == "unpaired") {
    return KnownDeviceState::kUnpaired;
  }
  return std::nullopt;
}

/// Encodes `time` as whole seconds since the Unix epoch.
std::int64_t EncodeEpochSeconds(std::chrono::system_clock::time_point time) {
  return std::chrono::duration_cast<std::chrono::seconds>(
             time.time_since_epoch())
      .count();
}

/// Decodes a JSON integer value as whole seconds since the epoch, or
/// `std::nullopt` when `value` is absent or not an integer.
std::optional<std::int64_t>
DecodeEpochSeconds(const boost::json::value *value) {
  if (!value || (!value->is_int64() && !value->is_uint64())) {
    return std::nullopt;
  }
  return value->is_int64() ? value->get_int64()
                           : static_cast<std::int64_t>(value->get_uint64());
}

/// Encodes a snapshot as JSON. `credential` is hex-encoded (JSON has no binary
/// type); `shortId`, `clientId`, and `state` are already text; `createdAt` is
/// whole seconds since the Unix epoch. `displayName` is a string or JSON
/// `null`; `blockedAt` is whole seconds since the epoch or JSON `null` when the
/// device is not currently `kBlocked`.
std::string EncodeSnapshotToJson(const TrustStoreSnapshot &snapshot) {
  boost::json::array devices;
  for (const auto &device : snapshot.devices) {
    boost::json::object obj;
    obj["clientId"] = device.clientId;
    obj["credential"] = EncodeHex(device.credential);
    obj["shortId"] = device.shortId;
    obj["displayName"] = device.displayName.has_value()
                             ? boost::json::value(*device.displayName)
                             : boost::json::value(nullptr);
    obj["state"] = EncodeState(device.state);
    obj["createdAt"] = EncodeEpochSeconds(device.createdAt);
    obj["blockedAt"] =
        device.blockedAt.has_value()
            ? boost::json::value(EncodeEpochSeconds(*device.blockedAt))
            : boost::json::value(nullptr);
    devices.push_back(std::move(obj));
  }

  boost::json::object root;
  root["devices"] = std::move(devices);
  return boost::json::serialize(root);
}

/// Decodes one device record from its JSON object form, or `std::nullopt` for
/// any shape mismatch or impossible state/credential/blockedAt combination
/// (fail closed on semantically corrupt content, not just structurally
/// malformed content). Content trustworthiness that this function does not
/// itself re-derive (e.g. that `displayName` still satisfies its bound) is
/// guaranteed by DPAPI's authenticated decryption having already succeeded on
/// this exact file.
std::optional<KnownDeviceRecord> DecodeRecord(const boost::json::value &item) {
  if (!item.is_object()) {
    return std::nullopt;
  }
  const auto &obj = item.get_object();
  const boost::json::value *clientIdValue = obj.if_contains("clientId");
  const boost::json::value *credentialValue = obj.if_contains("credential");
  const boost::json::value *shortIdValue = obj.if_contains("shortId");
  const boost::json::value *displayNameValue = obj.if_contains("displayName");
  const boost::json::value *stateValue = obj.if_contains("state");
  const boost::json::value *createdAtValue = obj.if_contains("createdAt");
  const boost::json::value *blockedAtValue = obj.if_contains("blockedAt");
  if (!clientIdValue || !clientIdValue->is_string() || !credentialValue ||
      !credentialValue->is_string() || !shortIdValue ||
      !shortIdValue->is_string() || !displayNameValue || !stateValue ||
      !stateValue->is_string() || !blockedAtValue) {
    return std::nullopt;
  }

  auto createdAtSeconds = DecodeEpochSeconds(createdAtValue);
  if (!createdAtSeconds.has_value()) {
    return std::nullopt;
  }

  std::optional<std::chrono::system_clock::time_point> blockedAt;
  if (!blockedAtValue->is_null()) {
    auto blockedAtSeconds = DecodeEpochSeconds(blockedAtValue);
    if (!blockedAtSeconds.has_value()) {
      return std::nullopt;
    }
    blockedAt = std::chrono::system_clock::time_point(
        std::chrono::seconds(*blockedAtSeconds));
  }

  // A non-kTrusted device's credential was securely cleared before this
  // snapshot was built and encodes as an empty string; DecodeHex rejects empty
  // text (there is no such thing as an empty *real* credential), so that case
  // is decoded directly instead of through DecodeHex.
  std::vector<std::uint8_t> credential;
  if (!credentialValue->get_string().empty()) {
    auto decoded = DecodeHex(credentialValue->get_string());
    if (!decoded.has_value()) {
      return std::nullopt;
    }
    credential = std::move(*decoded);
  }

  auto state = DecodeState(stateValue->get_string());
  if (!state.has_value()) {
    return std::nullopt;
  }

  // Fail closed on an impossible device-record combination rather than trusting
  // a structurally well-formed but semantically corrupt file: only a kTrusted
  // device may hold a real credential (every other state's credential is
  // securely cleared to empty by TrustStore before the snapshot is built, per
  // the comment above), and only a kBlocked device may carry a blockedAt.
  if (*state != KnownDeviceState::kTrusted && !credential.empty()) {
    return std::nullopt;
  }
  if (*state != KnownDeviceState::kBlocked && blockedAt.has_value()) {
    return std::nullopt;
  }

  std::optional<std::string> displayName;
  if (displayNameValue->is_string()) {
    displayName = std::string(displayNameValue->get_string());
  } else if (!displayNameValue->is_null()) {
    return std::nullopt;
  }

  return KnownDeviceRecord{
      .clientId = std::string(clientIdValue->get_string()),
      .credential = std::move(credential),
      .shortId = std::string(shortIdValue->get_string()),
      .displayName = std::move(displayName),
      .state = *state,
      .createdAt = std::chrono::system_clock::time_point(
          std::chrono::seconds(*createdAtSeconds)),
      .blockedAt = blockedAt,
  };
}

/// Decodes a full snapshot from JSON text, or `std::nullopt` for any structural
/// mismatch.
std::optional<TrustStoreSnapshot>
DecodeSnapshotFromJson(std::string_view text) {
  boost::system::error_code ec;
  boost::json::value parsed = boost::json::parse(text, ec);
  if (ec || !parsed.is_object()) {
    return std::nullopt;
  }
  const boost::json::object &root = parsed.get_object();

  const boost::json::value *devicesValue = root.if_contains("devices");
  if (!devicesValue || !devicesValue->is_array()) {
    return std::nullopt;
  }

  TrustStoreSnapshot snapshot;
  for (const auto &item : devicesValue->get_array()) {
    auto device = DecodeRecord(item);
    if (!device.has_value()) {
      return std::nullopt;
    }
    snapshot.devices.push_back(std::move(*device));
  }
  return snapshot;
}

/// Reads the complete contents of `path`, or `std::nullopt` when it cannot be
/// opened or read.
std::optional<std::vector<std::uint8_t>>
ReadFileBytes(const std::filesystem::path &path) {
  std::ifstream file(path, std::ios::binary);
  if (!file.is_open()) {
    return std::nullopt;
  }
  std::vector<std::uint8_t> bytes((std::istreambuf_iterator<char>(file)),
                                  std::istreambuf_iterator<char>());
  if (file.bad()) {
    return std::nullopt;
  }
  return bytes;
}

/// Overwrites `path` with `bytes`, truncating any existing content.
/// @return Whether the write succeeded.
bool WriteFileBytes(const std::filesystem::path &path,
                    const std::vector<std::uint8_t> &bytes) {
  std::ofstream file(path, std::ios::binary | std::ios::trunc);
  if (!file.is_open()) {
    return false;
  }
  file.write(reinterpret_cast<const char *>(bytes.data()),
             static_cast<std::streamsize>(bytes.size()));
  return file.good();
}

} // namespace

WindowsTrustStorePersistence::WindowsTrustStorePersistence(
    std::filesystem::path path)
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
  return DecodeSnapshotFromJson(
      std::string(decrypted->begin(), decrypted->end()));
}

bool WindowsTrustStorePersistence::Save(const TrustStoreSnapshot &snapshot) {
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

  if (!MoveFileExW(tempPath.c_str(), path_.c_str(),
                   MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
    std::error_code cleanupEc;
    std::filesystem::remove(tempPath, cleanupEc);
    return false;
  }
  return true;
}

std::optional<std::filesystem::path> ResolveDefaultTrustStorePath() {
  constexpr DWORD kMaxEnvValueChars = 4096;
  std::wstring buffer(kMaxEnvValueChars, L'\0');
  DWORD written = GetEnvironmentVariableW(L"LOCALAPPDATA", buffer.data(),
                                          kMaxEnvValueChars);
  if (written == 0 || written >= kMaxEnvValueChars) {
    return std::nullopt;
  }
  buffer.resize(written);

  std::filesystem::path path(buffer);
  path /= L"DovahLink";
  path /= L"trust_store.dat";
  return path;
}

} // namespace dovahlink::security

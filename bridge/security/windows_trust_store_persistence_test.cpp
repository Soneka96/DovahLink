#include "security/windows_trust_store_persistence.hpp"

#include "security/csprng.hpp"
#include "security/dpapi.hpp"

#include <catch2/catch_test_macros.hpp>

#include <windows.h>

#include <cstdint>
#include <filesystem>
#include <fstream>
#include <optional>
#include <string>
#include <vector>

using dovahlink::security::EncryptForCurrentUser;
using dovahlink::security::RevocationTombstone;
using dovahlink::security::TrustedClientRecord;
using dovahlink::security::TrustStoreSnapshot;
using dovahlink::security::WindowsTrustStorePersistence;

namespace {

/// A fresh, uniquely named temp directory for one test's file I/O, removed on destruction.
class TempDir {
public:
    TempDir()
        : path_(std::filesystem::temp_directory_path() /
                ("dovahlink_trust_store_test_" + dovahlink::security::GenerateOpaqueId().value())) {
        std::filesystem::create_directories(path_);
    }

    ~TempDir() {
        std::error_code ec;
        std::filesystem::remove_all(path_, ec);
    }

    TempDir(const TempDir&) = delete;
    TempDir& operator=(const TempDir&) = delete;

    /// The path a target trust-store file may be created under.
    [[nodiscard]] std::filesystem::path Path() const { return path_; }

private:
    std::filesystem::path path_;
};

/// Builds a deterministic credential-sized byte sequence from a seed value, including a zero byte
/// to exercise hex round-tripping of non-printable content.
std::vector<std::uint8_t> MakeCredential(std::uint8_t seed) {
    return std::vector<std::uint8_t>{seed, 0, static_cast<std::uint8_t>(seed + 1)};
}

/// Writes raw bytes directly to `path`, bypassing `WindowsTrustStorePersistence::Save` -- used to
/// craft deliberately corrupt or malformed fixtures.
void WriteRawFile(const std::filesystem::path& path, const std::vector<std::uint8_t>& bytes) {
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    file.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
}

/// Encrypts `json` as `WindowsTrustStorePersistence::Save` would and writes it directly to
/// `path`, bypassing the real JSON encoding step -- used to craft fixtures with well-formed
/// ciphertext but a deliberately malformed decrypted shape.
void WriteEncryptedJsonFile(const std::filesystem::path& path, const std::string& json) {
    auto encrypted = EncryptForCurrentUser(std::vector<std::uint8_t>(json.begin(), json.end()));
    REQUIRE(encrypted.has_value());
    WriteRawFile(path, *encrypted);
}

}  // namespace

TEST_CASE("Load on a nonexistent file returns a valid empty snapshot",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    WindowsTrustStorePersistence persistence(dir.Path() / "trust_store.dat");

    auto snapshot = persistence.Load();

    REQUIRE(snapshot.has_value());
    CHECK(snapshot->records.empty());
    CHECK(snapshot->tombstones.empty());
}

TEST_CASE("Save then Load round-trips a full TrustStoreSnapshot with exact field equality, "
          "including credential byte-vector equality",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    WindowsTrustStorePersistence persistence(dir.Path() / "trust_store.dat");

    TrustStoreSnapshot snapshot;
    snapshot.records = {
        TrustedClientRecord{.clientId = "client-1",
                             .credential = MakeCredential(1),
                             .shortId = "11111",
                             .displayName = std::string("My PC")},
        TrustedClientRecord{.clientId = "client-2",
                             .credential = MakeCredential(2),
                             .shortId = "22222",
                             .displayName = std::nullopt},
    };
    snapshot.tombstones = {RevocationTombstone{.clientId = "client-3"}};

    REQUIRE(persistence.Save(snapshot));
    auto loaded = persistence.Load();

    REQUIRE(loaded.has_value());
    REQUIRE(loaded->records.size() == 2);
    CHECK(loaded->records[0].clientId == "client-1");
    CHECK(loaded->records[0].credential == MakeCredential(1));
    CHECK(loaded->records[0].shortId == "11111");
    REQUIRE(loaded->records[0].displayName.has_value());
    CHECK(*loaded->records[0].displayName == "My PC");
    CHECK(loaded->records[1].clientId == "client-2");
    CHECK(loaded->records[1].credential == MakeCredential(2));
    CHECK_FALSE(loaded->records[1].displayName.has_value());
    REQUIRE(loaded->tombstones.size() == 1);
    CHECK(loaded->tombstones[0].clientId == "client-3");
}

TEST_CASE("Save creates a missing parent directory", "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "nested" / "does" / "not" / "exist" / "trust_store.dat";
    WindowsTrustStorePersistence persistence(path);

    CHECK(persistence.Save(TrustStoreSnapshot{}));
    CHECK(std::filesystem::exists(path));
}

TEST_CASE("Save leaves no temp file behind and produces a loadable target file",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WindowsTrustStorePersistence persistence(path);

    REQUIRE(persistence.Save(TrustStoreSnapshot{}));

    CHECK(std::filesystem::exists(path));
    CHECK_FALSE(std::filesystem::exists(dir.Path() / "trust_store.dat.tmp"));
}

TEST_CASE("a second Save fully replaces the first snapshot's content",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    WindowsTrustStorePersistence persistence(dir.Path() / "trust_store.dat");

    TrustStoreSnapshot first;
    first.records = {TrustedClientRecord{
        .clientId = "client-1", .credential = MakeCredential(1), .shortId = "11111", .displayName = std::nullopt}};
    REQUIRE(persistence.Save(first));

    TrustStoreSnapshot second;
    second.records = {TrustedClientRecord{
        .clientId = "client-2", .credential = MakeCredential(2), .shortId = "22222", .displayName = std::nullopt}};
    REQUIRE(persistence.Save(second));

    auto loaded = persistence.Load();

    REQUIRE(loaded.has_value());
    REQUIRE(loaded->records.size() == 1);
    CHECK(loaded->records[0].clientId == "client-2");
}

TEST_CASE("Load fails closed on a file that is not valid DPAPI ciphertext",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteRawFile(path, {0xDE, 0xAD, 0xBE, 0xEF});
    WindowsTrustStorePersistence persistence(path);

    auto loaded = persistence.Load();

    CHECK_FALSE(loaded.has_value());
}

TEST_CASE("Load fails closed on ciphertext that decrypts to non-JSON bytes",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(path, "definitely not json");
    WindowsTrustStorePersistence persistence(path);

    auto loaded = persistence.Load();

    CHECK_FALSE(loaded.has_value());
}

TEST_CASE("Load fails closed when a record is missing its credential field",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(
        path, R"({"records": [{"clientId": "client-1", "shortId": "11111", "displayName": null}], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a record is missing its shortId field",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(
        path,
        R"({"records": [{"clientId": "client-1", "credential": "01", "displayName": null}], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a record's displayName key is entirely absent",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(
        path, R"({"records": [{"clientId": "client-1", "credential": "01", "shortId": "11111"}], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a record's displayName has the wrong type",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(
        path,
        R"({"records": [{"clientId": "client-1", "credential": "01", "shortId": "11111", "displayName": 42}], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a record's credential is not valid hex",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(
        path,
        R"({"records": [{"clientId": "client-1", "credential": "not-hex", "shortId": "11111", "displayName": null}], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a record array item is not a JSON object",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(path, R"({"records": ["not-an-object"], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a tombstone is missing its clientId field",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(path, R"({"records": [], "tombstones": [{}]})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when a tombstone's clientId has the wrong type",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(path, R"({"records": [], "tombstones": [{"clientId": 123}]})");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed when the decrypted root JSON is not an object",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(path, R"(["records", "tombstones"])");
    WindowsTrustStorePersistence persistence(path);

    CHECK_FALSE(persistence.Load().has_value());
}

TEST_CASE("Load fails closed on well-formed JSON missing the required shape",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WriteEncryptedJsonFile(path, R"({"records": [{"clientId": "client-1"}], "tombstones": []})");
    WindowsTrustStorePersistence persistence(path);

    auto loaded = persistence.Load();

    CHECK_FALSE(loaded.has_value());
}

TEST_CASE("Save then Load round-trips an empty TrustStoreSnapshot",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    WindowsTrustStorePersistence persistence(dir.Path() / "trust_store.dat");

    REQUIRE(persistence.Save(TrustStoreSnapshot{}));
    auto loaded = persistence.Load();

    REQUIRE(loaded.has_value());
    CHECK(loaded->records.empty());
    CHECK(loaded->tombstones.empty());
}

TEST_CASE("a failed Save leaves the previously saved file untouched",
          "[security][windows_trust_store_persistence]") {
    TempDir dir;
    auto path = dir.Path() / "trust_store.dat";
    WindowsTrustStorePersistence persistence(path);

    TrustStoreSnapshot original;
    original.records = {TrustedClientRecord{
        .clientId = "client-1", .credential = MakeCredential(1), .shortId = "11111", .displayName = std::nullopt}};
    REQUIRE(persistence.Save(original));

    // Exclusively locks the temp file Save must write to next, forcing that write step to fail
    // without touching the already-saved target file.
    auto tempPath = path;
    tempPath += L".tmp";
    HANDLE lock = CreateFileW(tempPath.c_str(), GENERIC_WRITE, /*dwShareMode=*/0, nullptr, CREATE_ALWAYS,
                               FILE_ATTRIBUTE_NORMAL, nullptr);
    REQUIRE(lock != INVALID_HANDLE_VALUE);

    TrustStoreSnapshot different;
    different.records = {TrustedClientRecord{
        .clientId = "client-2", .credential = MakeCredential(2), .shortId = "22222", .displayName = std::nullopt}};
    CHECK_FALSE(persistence.Save(different));

    CloseHandle(lock);

    auto loaded = persistence.Load();
    REQUIRE(loaded.has_value());
    REQUIRE(loaded->records.size() == 1);
    CHECK(loaded->records[0].clientId == "client-1");
}

TEST_CASE("ResolveDefaultTrustStorePath returns a path under DovahLink",
          "[security][windows_trust_store_persistence]") {
    auto path = dovahlink::security::ResolveDefaultTrustStorePath();

    REQUIRE(path.has_value());
    CHECK(path->filename() == "trust_store.dat");
    CHECK(path->parent_path().filename() == "DovahLink");
}

#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>

namespace dovahlink::application {

///  Runtime compatibility toggles read from the bridge's optional INI file.
///  Both fields default to enabled: a missing file, a missing key, or a
///  malformed value all fall back to this same default per-key rather than
///  failing plugin load.
struct GameBehaviorConfig {
    ///  Whether Skyrim's `bAlwaysActive:General` setting should be forced on
    ///  so gameplay continues while the DovahLink window has focus.
    bool alwaysActive = true;
    ///  Whether the achievement-eligibility runtime patch should be installed
    ///  so achievements remain available with SKSE plugins loaded.
    bool achievementCompat = true;
};

///  Abstracts reading the compatibility INI file's raw text so parsing can be
///  tested without real filesystem access.
class GameBehaviorConfigFileReader {
  public:
    ///  Allows destruction through the interface.
    virtual ~GameBehaviorConfigFileReader() = default;

    ///  Returns the file's full text, or `std::nullopt` if it does not exist
    ///  or cannot be read.
    [[nodiscard]] virtual std::optional<std::string>
    Read(const std::filesystem::path& path) const = 0;
};

///  Reads the compatibility INI file from the real filesystem.
class FilesystemGameBehaviorConfigFileReader
    : public GameBehaviorConfigFileReader {
  public:
    ///  @copydoc GameBehaviorConfigFileReader::Read
    [[nodiscard]] std::optional<std::string>
    Read(const std::filesystem::path& path) const override;
};

///  Parses the `[DovahLink]` section of `reader`'s file at `path` into a
///  `GameBehaviorConfig`, defaulting each key independently when the file is
///  missing, the section or key is absent, or the value is not `0`/`1`.
///  @param reader Source of the file's raw text.
///  @param path Path to the compatibility INI file.
[[nodiscard]] GameBehaviorConfig
ReadGameBehaviorConfig(const GameBehaviorConfigFileReader& reader,
                       const std::filesystem::path& path);

} //  namespace dovahlink::application

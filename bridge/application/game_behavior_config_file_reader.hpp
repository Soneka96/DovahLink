#pragma once

#include "application/game_behavior_config.hpp"

#include <filesystem>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Abstracts reading the compatibility INI file's raw text so parsing can be
///  tested without real filesystem access.
class IGameBehaviorConfigFileReader {
  public:
    ///  Allows destruction through the interface.
    virtual ~IGameBehaviorConfigFileReader() = default;

    ///  Returns the file's full text, or `std::nullopt` if it does not exist
    ///  or cannot be read.
    [[nodiscard]] virtual std::optional<std::string>
    Read(const std::filesystem::path& path) const = 0;
};

///  Reads the compatibility INI file from the real filesystem.
class FilesystemGameBehaviorConfigFileReader
    : public IGameBehaviorConfigFileReader {
  public:
    ///  @copydoc IGameBehaviorConfigFileReader::Read
    [[nodiscard]] std::optional<std::string>
    Read(const std::filesystem::path& path) const override;
};

///  Parses the `[DovahLink]` section of `reader`'s file at `path` into a
///  `GameBehaviorConfig`, defaulting each key independently when the file is
///  missing, the section or key is absent, or the value is not `0`/`1`.
///  @param reader Source of the file's raw text.
///  @param path Path to the compatibility INI file.
[[nodiscard]] GameBehaviorConfig
ReadGameBehaviorConfig(const IGameBehaviorConfigFileReader& reader,
                       const std::filesystem::path& path);

} //  namespace dovahlink::application

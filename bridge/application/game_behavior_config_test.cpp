#include "application/game_behavior_config.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>
#include <unordered_map>
#include <utility>

using dovahlink::application::GameBehaviorConfig;
using dovahlink::application::GameBehaviorConfigFileReader;
using dovahlink::application::ReadGameBehaviorConfig;

namespace {

constexpr const char *kPath = "Data/SKSE/Plugins/DovahLinkBridge.ini";

/// Serves fixed file text for one path, without touching the real filesystem.
class FakeGameBehaviorConfigFileReader : public GameBehaviorConfigFileReader {
public:
  /// @copydoc GameBehaviorConfigFileReader::Read
  [[nodiscard]] std::optional<std::string>
  Read(const std::filesystem::path &path) const override {
    auto it = files_.find(path.generic_string());
    if (it == files_.end()) {
      return std::nullopt;
    }
    return it->second;
  }

  /// Sets the text returned for `path`.
  void Set(const std::filesystem::path &path, std::string text) {
    files_[path.generic_string()] = std::move(text);
  }

private:
  /// Fixed file contents keyed by generic-format path.
  std::unordered_map<std::string, std::string> files_;
};

} // namespace

TEST_CASE("ReadGameBehaviorConfig defaults both flags to true when the file is "
          "missing",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig reads both keys set to 0",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\nbAchievementCompat=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig reads both keys set to 1",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=1\nbAchievementCompat=1\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig treats each key independently",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig defaults a key with a malformed value "
          "instead of failing",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=maybe\nbAchievementCompat=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig ignores keys outside the [DovahLink] section",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath,
             "[General]\nbAlwaysActive=0\n[DovahLink]\nbAchievementCompat=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig tolerates comments, blank lines, and "
          "surrounding whitespace",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "; DovahLink compatibility settings\n"
                    "\n"
                    "[DovahLink]\n"
                    "  bAlwaysActive = 0  \n"
                    "# a comment line\n"
                    "\tbAchievementCompat=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig ignores an unrecognized key",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbSomethingElse=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig treats the section header as case-sensitive",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[dovahlink]\nbAlwaysActive=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
}

TEST_CASE(
    "ReadGameBehaviorConfig stops applying keys once a later section begins",
    "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath,
             "[DovahLink]\nbAlwaysActive=0\n[Other]\nbAchievementCompat=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE(
    "ReadGameBehaviorConfig lets a repeated section header's keys apply again",
    "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\n[Other]\n[DovahLink]"
                    "\nbAchievementCompat=0\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadGameBehaviorConfig lets a later duplicate key win",
          "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\nbAlwaysActive=1\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
}

TEST_CASE(
    "ReadGameBehaviorConfig strips a trailing inline comment from a value",
    "[application][game_behavior_config]") {
  FakeGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0 ; disabled for "
                    "testing\nbAchievementCompat=1 # note\n");

  GameBehaviorConfig config = ReadGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK(config.achievementCompat);
}

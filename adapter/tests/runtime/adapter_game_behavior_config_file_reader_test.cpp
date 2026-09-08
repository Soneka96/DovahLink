#include "runtime/adapter_game_behavior_config_file_reader.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <string>
#include <unordered_map>
#include <utility>

using dovahlink::adapter::runtime::AdapterGameBehaviorConfig;
using dovahlink::adapter::runtime::IAdapterGameBehaviorConfigFileReader;
using dovahlink::adapter::runtime::ReadAdapterGameBehaviorConfig;

namespace {

constexpr const char *kPath = "Data/SKSE/Plugins/DovahLinkAdapter.ini";

///  Serves fixed file text for one path, without touching the real filesystem.
class FakeAdapterGameBehaviorConfigFileReader
    : public IAdapterGameBehaviorConfigFileReader {
public:
  ///  @copydoc IAdapterGameBehaviorConfigFileReader::Read
  [[nodiscard]] std::optional<std::string>
  Read(const std::filesystem::path &path) const override {
    auto it = files_.find(path.generic_string());
    if (it == files_.end()) {
      return std::nullopt;
    }
    return it->second;
  }

  ///  Sets the text returned for `path`.
  void Set(const std::filesystem::path &path, std::string text) {
    files_[path.generic_string()] = std::move(text);
  }

private:
  ///  Fixed file contents keyed by generic-format path.
  std::unordered_map<std::string, std::string> files_;
};

} //  namespace

TEST_CASE(
    "ReadAdapterGameBehaviorConfig defaults both flags to true when the file "
    "is missing",
    "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig reads both keys set to 0",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\nbAchievementCompat=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig reads both keys set to 1",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=1\nbAchievementCompat=1\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig treats each key independently",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig defaults a key with a malformed value "
          "instead of failing",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=maybe\nbAchievementCompat=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig ignores keys outside the [DovahLink] "
          "section",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath,
             "[General]\nbAlwaysActive=0\n[DovahLink]\nbAchievementCompat=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig tolerates comments, blank lines, and "
          "surrounding whitespace",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "; DovahLink compatibility settings\n"
                    "\n"
                    "[DovahLink]\n"
                    "  bAlwaysActive = 0  \n"
                    "# a comment line\n"
                    "\tbAchievementCompat=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig ignores an unrecognized key",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbSomethingElse=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig treats the section header as "
          "case-sensitive",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[dovahlink]\nbAlwaysActive=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
}

TEST_CASE(
    "ReadAdapterGameBehaviorConfig stops applying keys once a later section "
    "begins",
    "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath,
             "[DovahLink]\nbAlwaysActive=0\n[Other]\nbAchievementCompat=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig lets a repeated section header's keys "
          "apply again",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\n[Other]\n[DovahLink]"
                    "\nbAchievementCompat=0\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK_FALSE(config.achievementCompat);
}

TEST_CASE("ReadAdapterGameBehaviorConfig lets a later duplicate key win",
          "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0\nbAlwaysActive=1\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK(config.alwaysActive);
}

TEST_CASE(
    "ReadAdapterGameBehaviorConfig strips a trailing inline comment from a "
    "value",
    "[runtime][adapter_game_behavior_config_file_reader]") {
  FakeAdapterGameBehaviorConfigFileReader reader;
  reader.Set(kPath, "[DovahLink]\nbAlwaysActive=0 ; disabled for "
                    "testing\nbAchievementCompat=1 # note\n");

  AdapterGameBehaviorConfig config =
      ReadAdapterGameBehaviorConfig(reader, kPath);

  CHECK_FALSE(config.alwaysActive);
  CHECK(config.achievementCompat);
}

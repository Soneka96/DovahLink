#include "runtime/adapter_game_behavior_config_file_reader.hpp"

#include "runtime/adapter_runtime_constants.hpp"

#include <fstream>
#include <sstream>

namespace dovahlink::adapter::runtime {

namespace {

///  Returns `text` with leading and trailing ASCII whitespace removed.
std::string_view Trim(std::string_view text) {
  const auto first = text.find_first_not_of(" \t\r\n");
  if (first == std::string_view::npos) {
    return {};
  }
  const auto last = text.find_last_not_of(" \t\r\n");
  return text.substr(first, last - first + 1);
}

///  Applies one parsed `key=value` pair to `config` if it matches a known
///  compatibility key with a valid `0`/`1` value; otherwise leaves `config`
///  unchanged for that key.
void ApplyKey(std::string_view key, std::string_view value,
              AdapterGameBehaviorConfig &config) {
  std::optional<bool> parsed;
  if (value == "1") {
    parsed = true;
  } else if (value == "0") {
    parsed = false;
  } else {
    return;
  }

  if (key == kAdapterAlwaysActiveKey) {
    config.alwaysActive = *parsed;
  } else if (key == kAdapterAchievementCompatKey) {
    config.achievementCompat = *parsed;
  }
}

///  Parses `text` as INI content, applying only keys found inside the
///  `[DovahLink]` section to `config`.
AdapterGameBehaviorConfig
ParseAdapterGameBehaviorConfig(std::string_view text) {
  AdapterGameBehaviorConfig config;
  bool inSection = false;
  std::istringstream stream{std::string(text)};
  std::string rawLine;
  while (std::getline(stream, rawLine)) {
    std::string_view line = Trim(rawLine);
    if (line.empty() || line.front() == ';' || line.front() == '#') {
      continue;
    }
    if (line.front() == '[') {
      inSection =
          line == std::string("[") + kAdapterGameBehaviorConfigSection + "]";
      continue;
    }
    if (!inSection) {
      continue;
    }
    const auto separator = line.find('=');
    if (separator == std::string_view::npos) {
      continue;
    }
    std::string_view value = Trim(line.substr(separator + 1));
    const auto commentStart = value.find_first_of(";#");
    if (commentStart != std::string_view::npos) {
      value = Trim(value.substr(0, commentStart));
    }
    ApplyKey(Trim(line.substr(0, separator)), value, config);
  }
  return config;
}

} //  namespace

std::optional<std::string> FilesystemAdapterGameBehaviorConfigFileReader::Read(
    const std::filesystem::path &path) const {
  std::ifstream file(path);
  if (!file.is_open()) {
    return std::nullopt;
  }
  std::ostringstream buffer;
  buffer << file.rdbuf();
  return buffer.str();
}

AdapterGameBehaviorConfig ReadAdapterGameBehaviorConfig(
    const IAdapterGameBehaviorConfigFileReader &reader,
    const std::filesystem::path &path) {
  auto text = reader.Read(path);
  if (!text.has_value()) {
    return AdapterGameBehaviorConfig{};
  }
  return ParseAdapterGameBehaviorConfig(*text);
}

} //  namespace dovahlink::adapter::runtime

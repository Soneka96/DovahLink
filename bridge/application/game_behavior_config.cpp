#include "application/game_behavior_config.hpp"

#include "application/bridge_config.hpp"

#include <fstream>
#include <sstream>

namespace dovahlink::application {

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
              GameBehaviorConfig& config) {
    std::optional<bool> parsed;
    if (value == "1") {
        parsed = true;
    } else if (value == "0") {
        parsed = false;
    } else {
        return;
    }

    if (key == kAlwaysActiveKey) {
        config.alwaysActive = *parsed;
    } else if (key == kAchievementCompatKey) {
        config.achievementCompat = *parsed;
    }
}

///  Parses `text` as INI content, applying only keys found inside the
///  `[DovahLink]` section to `config`.
GameBehaviorConfig ParseGameBehaviorConfig(std::string_view text) {
    GameBehaviorConfig config;
    bool inSection = false;
    std::istringstream stream{std::string(text)};
    std::string rawLine;
    while (std::getline(stream, rawLine)) {
        std::string_view line = Trim(rawLine);
        if (line.empty() || line.front() == ';' || line.front() == '#') {
            continue;
        }
        if (line.front() == '[') {
            inSection = line == std::string("[") + kGameBehaviorConfigSection + "]";
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

std::optional<std::string> FilesystemGameBehaviorConfigFileReader::Read(
    const std::filesystem::path& path) const {
    std::ifstream file(path);
    if (!file.is_open()) {
        return std::nullopt;
    }
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

GameBehaviorConfig
ReadGameBehaviorConfig(const GameBehaviorConfigFileReader& reader,
                       const std::filesystem::path& path) {
    auto text = reader.Read(path);
    if (!text.has_value()) {
        return GameBehaviorConfig{};
    }
    return ParseGameBehaviorConfig(*text);
}

} //  namespace dovahlink::application

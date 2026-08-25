#pragma once

#include <cstdint>

namespace dovahlink::application {

///  Default loopback port used by the bridge and validation harness.
inline constexpr std::uint16_t kBridgePort = 58231;

///  The DovahLink Bridge/mod release version, exposed to clients in
///  `hello_ack.bridgeVersion` (ai/context/protocol/compatibility.md). Must match
///  bridge/vcpkg.json's version-string; `tooling/test_repository_consistency.py`
///  guards that.
inline constexpr const char* kBridgeVersion = "0.3.3";

///  Environment variable containing the development connection token.
inline constexpr const char* kTokenEnvVar = "DOVAHLINK_BRIDGE_TOKEN";

///  Path to the optional runtime-compatibility INI file, relative to the
///  Skyrim installation's working directory.
inline constexpr const char* kGameBehaviorConfigPath =
    "Data/SKSE/Plugins/DovahLinkBridge.ini";

///  INI section `game_behavior_config.cpp` reads compatibility keys from.
inline constexpr const char* kGameBehaviorConfigSection = "DovahLink";

///  INI key controlling `GameBehaviorConfig::alwaysActive`.
inline constexpr const char* kAlwaysActiveKey = "bAlwaysActive";

///  INI key controlling `GameBehaviorConfig::achievementCompat`.
inline constexpr const char* kAchievementCompatKey = "bAchievementCompat";

} //  namespace dovahlink::application

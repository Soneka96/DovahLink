#pragma once

namespace dovahlink::adapter::runtime {

//  ---- Game behavior compatibility ----

///  Path to the optional runtime-compatibility INI file, relative to the
///  Skyrim installation's working directory.
inline constexpr const char *kAdapterGameBehaviorConfigPath =
    "Data/SKSE/Plugins/DovahLinkAdapter.ini";

///  INI section `DovahLink` from which
///  `adapter_game_behavior_config_file_reader.cpp` reads compatibility keys.
inline constexpr const char *kAdapterGameBehaviorConfigSection = "DovahLink";

///  INI key controlling `AdapterGameBehaviorConfig::alwaysActive`.
inline constexpr const char *kAdapterAlwaysActiveKey = "bAlwaysActive";

///  INI key controlling `AdapterGameBehaviorConfig::achievementCompat`.
inline constexpr const char *kAdapterAchievementCompatKey =
    "bAchievementCompat";

} //  namespace dovahlink::adapter::runtime

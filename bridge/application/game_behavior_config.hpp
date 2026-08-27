#pragma once

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

} //  namespace dovahlink::application

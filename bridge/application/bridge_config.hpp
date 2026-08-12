#pragma once

#include <cstdint>

namespace dovahlink::application {

// Shared between the real SKSE plugin entry point (plugin/dovahlink_bridge_plugin.cpp) and the
// Skyrim-independent test harness (harness/dovahlink_bridge_harness.cpp), so both listen on the
// same documented port and read the token from the same environment variable name without
// duplicating either value. See bridge/README.md's "Default loopback port" and "One-time token
// supply" sections for why these exact values were chosen.
inline constexpr std::uint16_t kBridgePort = 58231;
inline constexpr const char* kTokenEnvVar = "DOVAHLINK_BRIDGE_TOKEN";

}  // namespace dovahlink::application

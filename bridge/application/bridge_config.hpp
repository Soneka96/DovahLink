#pragma once

#include <cstdint>

namespace dovahlink::application {

/// Default loopback port used by the bridge and validation harness.
inline constexpr std::uint16_t kBridgePort = 58231;

/// Environment variable containing the development connection token.
inline constexpr const char* kTokenEnvVar = "DOVAHLINK_BRIDGE_TOKEN";

}  // namespace dovahlink::application

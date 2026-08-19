#pragma once

#include <cstdint>
#include <string>

namespace dovahlink::protocol {

/// Advertised capability identifier and version.
struct Capability {
    /// Stable capability identifier.
    std::string id;
    /// Capability schema or implementation version.
    std::int64_t version = 0;
};

}  // namespace dovahlink::protocol

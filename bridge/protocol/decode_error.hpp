#pragma once

#include <string>

namespace dovahlink::protocol {

/// Describes a protocol decode failure without including payload or secret data.
struct DecodeError {
    /// Human-readable reason safe to include in diagnostics.
    std::string reason;
};

}  // namespace dovahlink::protocol

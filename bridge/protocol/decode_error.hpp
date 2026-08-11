#pragma once

#include <string>

namespace dovahlink::protocol {

// A human-readable decode-failure reason; safe to log. Never includes payload
// content or secrets. Shared by every decode layer (envelope, message payloads).
struct DecodeError {
    std::string reason;
};

}  // namespace dovahlink::protocol

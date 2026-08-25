#pragma once

#include <string>

namespace dovahlink::protocol {

///  Describes a protocol decode failure without including payload or secret
///  data.
struct DecodeError {
    ///  Human-readable reason safe to include in diagnostics.
    std::string reason;
};

///  Reports a message-payload decoding failure. An alias, not a distinct type:
///  every payload codec's error is structurally a `DecodeError`.
using MessageError = DecodeError;

} //  namespace dovahlink::protocol

#pragma once

#include "protocol/envelope.hpp"

#include <boost/json/object.hpp>

#include <optional>
#include <string>

namespace dovahlink::protocol {

///  The reason payload of an unsolicited `session_invalidated` event, per
///  `ai/context/protocol/security.md`'s "Administrative session invalidation".
struct SessionInvalidatedPayload {
    ///  One of `"revoked"`, `"blocked"`, `"trust_reset"`, `"factory_reset"`.
    std::string reason;
};

///  Encodes a session-invalidated payload. Server-originated only; the client
///  never sends this message back, so no matching `Decode...` function exists.
boost::json::object
EncodeSessionInvalidatedPayload(const SessionInvalidatedPayload& payload);

///  Builds a complete, unsolicited `session_invalidated` envelope and always
///  returns a usable envelope. Uses the `csprng-unavailable` sentinel message ID
///  if secure ID generation fails, matching `BuildErrorEnvelope`.
///  `correlationId` is always null -- this event answers nothing.
///  @param sessionId The invalidated session's identifier.
///  @param reason One of `"revoked"`, `"blocked"`, `"trust_reset"`,
///  `"factory_reset"`.
Envelope BuildSessionInvalidatedEnvelope(std::optional<std::string> sessionId,
                                         std::string reason);

} //  namespace dovahlink::protocol

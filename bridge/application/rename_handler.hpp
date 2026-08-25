#pragma once

#include "protocol/envelope.hpp"
#include "security/trust_store.hpp"

#include <string>

namespace dovahlink::application {

/// Handles a client's authenticated `rename_request` for its own trusted-device
/// record.
/// @param renameRequestEnvelope Decoded client `rename_request`.
/// @param sessionId Authenticated session identifier.
/// @param clientId The connection's authenticated client identity.
/// @param trustStore Persistent trust store containing the client's
/// known-device record.
/// @return A `rename_outcome`, or a protocol `error` for malformed input or
/// persistence failure.
[[nodiscard]] protocol::Envelope
HandleRenameRequest(const protocol::Envelope &renameRequestEnvelope,
                    const std::string &sessionId, const std::string &clientId,
                    security::TrustStore &trustStore);

} // namespace dovahlink::application

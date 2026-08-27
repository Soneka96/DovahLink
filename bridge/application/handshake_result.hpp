#pragma once

#include "protocol/envelope.hpp"
#include "shared/scoped_release.hpp"

#include <optional>

namespace dovahlink::application {

///  Result of processing one client hello message.
struct HandshakeResult {
    ///  Response envelope to send to the client.
    protocol::Envelope response;

    ///  Ownership of the authenticated session on success.
    std::optional<shared::ScopedRelease> sessionLease;

    ///  Whether the connection must close after sending `response`.
    bool closeConnection = false;
};

} //  namespace dovahlink::application

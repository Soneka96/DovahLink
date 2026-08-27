#pragma once

#include "protocol/envelope.hpp"

#include <vector>

namespace dovahlink::application {

///  Contains responses produced while processing one inbound message.
struct DispatchResult {
    ///  Response envelopes to send in order.
    std::vector<protocol::Envelope> responses;

    ///  Whether the connection must close after responses are sent.
    bool closeConnection = false;
};

} //  namespace dovahlink::application

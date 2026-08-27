#pragma once

#include "protocol/envelope.hpp"

#include <string>
#include <vector>

namespace dovahlink::application {

///  Contains a subscription acknowledgement and its initial snapshots.
struct SubscribeResult {
    ///  Acknowledgement for the subscription request.
    protocol::Envelope subscriptionAck;

    ///  Snapshots for accepted state areas, in request order. Always empty: no
    ///  state area is currently registered.
    std::vector<protocol::Envelope> snapshots;

    ///  State areas accepted by this request, exposed structurally (beyond
    ///  `subscriptionAck`'s encoded payload) for the dispatcher's own
    ///  per-connection subscription bookkeeping. Always empty: no state area is
    ///  currently registered.
    std::vector<std::string> acceptedStateAreas;
};

} //  namespace dovahlink::application

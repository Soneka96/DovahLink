#pragma once

#include "protocol/envelope.hpp"

#include <string>

namespace dovahlink::application {

///  Receives typed publications from `IStatePublisher` toward the bounded
///  outbound organization. It is the handoff seam between publication-mode
///  dispatch and the bounded transport organization; `IStatePublisher` can
///  therefore remain complete and testable while that organization is wired
///  behind this contract.
class IOutboundPublicationSink {
  public:
    ///  Allows destruction through the interface.
    virtual ~IOutboundPublicationSink() = default;

    ///  Submits a replaceable Snapshot-mode envelope, replacing any pending
    ///  envelope previously submitted for the same state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param envelope Built `state_snapshot` envelope.
    virtual void PublishSnapshot(std::string stateArea,
                                 protocol::Envelope envelope) = 0;

    ///  Submits a reliable Event-mode envelope, appended in order and never
    ///  coalesced with another submission for the same state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param envelope Built `state_event` envelope.
    virtual void PublishEvent(std::string stateArea,
                              protocol::Envelope envelope) = 0;
};

} //  namespace dovahlink::application

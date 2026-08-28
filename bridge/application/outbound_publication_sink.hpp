#pragma once

#include "protocol/envelope.hpp"

#include <string>

namespace dovahlink::application {

///  Receives typed publications from `IStatePublisher` toward the bounded
///  outbound organization. This interface has no production implementation
///  yet -- the bounded outbound organization is a later stage's scope,
///  mirroring how `IRevisionTracker::NextEvent` was introduced before
///  event delivery had a caller. `IStatePublisher` depends on this contract
///  now so its own publication-mode dispatch is complete and testable ahead
///  of that implementation.
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

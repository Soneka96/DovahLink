#pragma once

#include "application/outbound_publication_sink.hpp"
#include "application/revision_tracker.hpp"

#include <boost/json/object.hpp>

#include <chrono>
#include <string>

namespace dovahlink::application {

///  Builds typed publications from captured state and submits them toward
///  the bounded outbound organization. One authoritative ordering point
///  (`RevisionTracker`) assigns each state area's revision, so publication
///  stays deterministically ordered even when captures arrive from
///  different runtime sources. Built envelopes carry no `sessionId`: this
///  publisher has no connection context, so the eventual live-session
///  writer stamps in the active session's identity at send time, the same
///  way `ConnectionSession::Run` stamps `bridgeInstanceId`/`playContextId`
///  onto `capabilities` after building it.
class IStatePublisher {
  public:
    ///  Allows destruction through the interface.
    virtual ~IStatePublisher() = default;

    ///  Publishes the current data for a replaceable Snapshot-mode state
    ///  area, replacing any pending publication for the same area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param data State-area-specific snapshot data.
    ///  @param occurredAt Wall-clock time the data was captured.
    ///  @return `true` when published; `false` only when the envelope could
    ///  not be built.
    virtual bool PublishSnapshot(
        const std::string& stateArea, boost::json::object data,
        std::chrono::system_clock::time_point occurredAt) = 0;

    ///  Publishes one ordered update for a reliable Event-mode state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param data State-area-specific event data.
    ///  @param occurredAt Wall-clock time the event occurred.
    ///  @return `true` when published; `false` when no snapshot baseline
    ///  exists yet for this state area, or the envelope could not be built.
    virtual bool
    PublishEvent(const std::string& stateArea, boost::json::object data,
                 std::chrono::system_clock::time_point occurredAt) = 0;
};

///  @copydoc IStatePublisher
class StatePublisher final : public IStatePublisher {
  public:
    ///  Binds the publisher to its revision authority and outbound sink.
    ///  `RevisionTracker` is the concrete type rather than
    ///  `IRevisionTracker` because `CommitSnapshotIfBuilt` -- required here
    ///  to assign a Snapshot's revision and build its envelope atomically
    ///  under one lock -- is declared only on the concrete class (see its
    ///  own doc comment for why the interface cannot express it).
    ///  @param revisionTracker Authoritative per-state-area revision
    ///  ordering.
    ///  @param sink Receives built publications.
    StatePublisher(RevisionTracker& revisionTracker,
                   IOutboundPublicationSink& sink);

    ///  @copydoc IStatePublisher::PublishSnapshot
    bool PublishSnapshot(const std::string& stateArea, boost::json::object data,
                         std::chrono::system_clock::time_point occurredAt)
        override;

    ///  @copydoc IStatePublisher::PublishEvent
    bool PublishEvent(const std::string& stateArea, boost::json::object data,
                      std::chrono::system_clock::time_point occurredAt)
        override;

  private:
    ///  Authoritative per-state-area revision ordering.
    RevisionTracker& revisionTracker_;

    ///  Receives built publications.
    IOutboundPublicationSink& sink_;
};

} //  namespace dovahlink::application

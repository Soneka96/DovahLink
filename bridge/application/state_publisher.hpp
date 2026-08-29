#pragma once

#include "application/outbound_publication_sink.hpp"
#include "application/revision_tracker.hpp"
#include "protocol/envelope.hpp"
#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <chrono>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>

namespace dovahlink::application {

///  Builds typed publications from captured state and submits them toward
///  the bounded outbound organization. One per-state-area publication gate
///  serializes revision assignment, envelope construction, and sink handoff
///  so publication stays deterministically ordered even when captures arrive
///  from different runtime sources -- and, for `PublishCapture` specifically,
///  so a state area's baseline-presence check and the publish it drives can
///  never be interleaved with a concurrent publish for the same area, by
///  construction rather than by relying on a caller's own threading model.
///  Revisions belong to whichever `IRevisionTracker` the caller passes per
///  call, not to this publisher instance: each play context owns its own
///  revision tracker, so a caller supplies the pinned context's tracker for
///  every publish rather than this class holding one for its whole lifetime.
///  Built envelopes carry the caller-supplied `playContextId` but no
///  `sessionId`: this publisher has no connection context, so the eventual
///  live-session writer stamps in the active session's identity at send
///  time, the same way `ConnectionSession::Run` stamps `bridgeInstanceId`
///  onto `capabilities` after building it.
class IStatePublisher {
  public:
    ///  Allows destruction through the interface.
    virtual ~IStatePublisher() = default;

    ///  Publishes the current data for a replaceable Snapshot-mode state
    ///  area, replacing any pending publication for the same area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param playContextId Identity of the play context this data came
    ///  from, stamped onto the built envelope.
    ///  @param revisionTracker Authoritative per-state-area revision
    ///  ordering for the play context this data came from, reached through
    ///  `CommitSnapshotEnvelopeIfBuilt` to assign a revision and build the
    ///  envelope atomically under one lock.
    ///  @param data State-area-specific snapshot data.
    ///  @param occurredAt Wall-clock time the data was captured.
    ///  @param stillCurrent Checked once the envelope is built, immediately
    ///  before it reaches the sink; a `false` result discards the publish
    ///  without rolling back the revision the build already consumed.
    ///  @return `true` when published; `false` when the envelope could not
    ///  be built, or `stillCurrent` returned `false`.
    virtual bool PublishSnapshot(
        const std::string& stateArea, const std::string& playContextId,
        IRevisionTracker& revisionTracker, boost::json::object data,
        std::chrono::system_clock::time_point occurredAt,
        const std::function<bool()>& stillCurrent) = 0;

    ///  Publishes one ordered update for a reliable Event-mode state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param playContextId Identity of the play context this data came
    ///  from, stamped onto the built envelope.
    ///  @param revisionTracker Authoritative per-state-area revision
    ///  ordering for the play context this data came from.
    ///  @param data State-area-specific event data.
    ///  @param occurredAt Wall-clock time the event occurred.
    ///  @param stillCurrent Checked once the envelope is built, immediately
    ///  before it reaches the sink; a `false` result discards the publish
    ///  without rolling back the revision the build already consumed.
    ///  @return `true` when published; `false` when no snapshot baseline
    ///  exists yet for this state area, the envelope could not be built, or
    ///  `stillCurrent` returned `false`.
    virtual bool
    PublishEvent(const std::string& stateArea, const std::string& playContextId,
                 IRevisionTracker& revisionTracker, boost::json::object data,
                 std::chrono::system_clock::time_point occurredAt,
                 const std::function<bool()>& stillCurrent) = 0;

    ///  Publishes one captured value, atomically deciding under one
    ///  per-state-area lock whether to establish a missing Snapshot baseline
    ///  or advance an existing one as an ordered Event -- the baseline check
    ///  and the publish it drives can never be interleaved with a
    ///  concurrent publish for the same area, regardless of caller
    ///  threading. `mode == kEvent` is honored only when `stateArea` already
    ///  has a revision baseline within `revisionTracker`; otherwise the same
    ///  `data` establishes that baseline as a Snapshot instead, never as
    ///  both. This relies on `data` already being complete post-change
    ///  state for `mode == kEvent`, per this protocol's own Event-mode
    ///  contract -- not a delta -- so the same value validly serves as
    ///  either delivery mode's payload.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param playContextId Identity of the play context this capture came
    ///  from, stamped onto the built envelope.
    ///  @param revisionTracker Authoritative per-state-area revision
    ///  ordering for the play context this capture came from.
    ///  @param mode Requested delivery mode.
    ///  @param data Captured, complete post-change state.
    ///  @param occurredAt Wall-clock time the value was captured.
    ///  @param stillCurrent Checked once the envelope is built, immediately
    ///  before it reaches the sink; a `false` result discards the publish
    ///  without rolling back the revision the build already consumed.
    ///  @return `true` when published; `false` when the envelope could not
    ///  be built, or `stillCurrent` returned `false`.
    virtual bool PublishCapture(
        const std::string& stateArea, const std::string& playContextId,
        IRevisionTracker& revisionTracker, CaptureMode mode,
        boost::json::object data,
        std::chrono::system_clock::time_point occurredAt,
        const std::function<bool()>& stillCurrent) = 0;
};

///  @copydoc IStatePublisher
class StatePublisher final : public IStatePublisher {
  public:
    ///  Binds the publisher to its outbound sink.
    ///  @param sink Receives built publications.
    explicit StatePublisher(IOutboundPublicationSink& sink);

    ///  @copydoc IStatePublisher::PublishSnapshot
    bool PublishSnapshot(const std::string& stateArea,
                         const std::string& playContextId,
                         IRevisionTracker& revisionTracker,
                         boost::json::object data,
                         std::chrono::system_clock::time_point occurredAt,
                         const std::function<bool()>& stillCurrent) override;

    ///  @copydoc IStatePublisher::PublishEvent
    bool PublishEvent(const std::string& stateArea,
                      const std::string& playContextId,
                      IRevisionTracker& revisionTracker,
                      boost::json::object data,
                      std::chrono::system_clock::time_point occurredAt,
                      const std::function<bool()>& stillCurrent) override;

    ///  @copydoc IStatePublisher::PublishCapture
    bool PublishCapture(const std::string& stateArea,
                        const std::string& playContextId,
                        IRevisionTracker& revisionTracker, CaptureMode mode,
                        boost::json::object data,
                        std::chrono::system_clock::time_point occurredAt,
                        const std::function<bool()>& stillCurrent) override;

  private:
    ///  Returns the stable publication mutex for one state area, creating it
    ///  while protecting the mutex map when this is the area's first use.
    [[nodiscard]] std::shared_ptr<std::mutex>
    PublicationMutexFor(const std::string& stateArea);

    ///  Builds a Snapshot envelope from `revisionTracker`'s next revision
    ///  for `stateArea`. Caller must already hold that area's publication
    ///  mutex.
    [[nodiscard]] std::optional<protocol::Envelope> BuildSnapshotEnvelopeLocked(
        const std::string& stateArea, IRevisionTracker& revisionTracker,
        boost::json::object data,
        std::chrono::system_clock::time_point occurredAt);

    ///  Builds an Event envelope advancing `revisionTracker`'s existing
    ///  baseline for `stateArea`, or no value when none exists yet. Caller
    ///  must already hold that area's publication mutex.
    [[nodiscard]] std::optional<protocol::Envelope> BuildEventEnvelopeLocked(
        const std::string& stateArea, IRevisionTracker& revisionTracker,
        boost::json::object data,
        std::chrono::system_clock::time_point occurredAt);

    ///  Receives built publications.
    IOutboundPublicationSink& sink_;

    ///  Synchronizes access to `publicationMutexes_`.
    std::mutex publicationMutexesMutex_;

    ///  Stable per-state-area gates that serialize publication through the sink.
    std::unordered_map<std::string, std::shared_ptr<std::mutex>>
        publicationMutexes_;
};

} //  namespace dovahlink::application

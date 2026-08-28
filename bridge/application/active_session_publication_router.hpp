#pragma once

#include "application/outbound_publication_sink.hpp"

#include <cstdint>
#include <mutex>
#include <string>

namespace dovahlink::application {

///  `StatePublisher`'s one `IOutboundPublicationSink`, routing every
///  publication toward whichever session's outbound queue is currently
///  attached, or dropping it when none is. Publications are dropped, not
///  queued, while no session is attached -- the same behavior as any other
///  moment when no session is connected
///  (`ai/context/skse/architecture.md`'s "Session and publication
///  ownership"): authoritative state and revisions inside `StatePublisher`
///  advance regardless, and the next attached session starts from a fresh
///  Snapshot rather than a replayed queue. `Attach`/`Detach` are declared
///  only on this concrete class, not on `IOutboundPublicationSink`, so a
///  consumer that only publishes depends on the narrower interface while the
///  session-binding caller depends on the concrete type -- the same split
///  `RevisionTracker` uses for its own template-only methods.
class ActiveSessionPublicationRouter final : public IOutboundPublicationSink {
  public:
    ///  Binds the currently attached sink, replacing any previous binding.
    ///  Does not take ownership; `sink` must outlive either this call's
    ///  matching `Detach()` or a later `Attach()` replacing it.
    ///  @param sink Session-scoped sink to route publications into.
    void Attach(IOutboundPublicationSink& sink);

    ///  Clears the currently attached sink, if any. Publications made after
    ///  this call are dropped until a new sink is attached.
    void Detach();

    ///  @copydoc IOutboundPublicationSink::PublishSnapshot
    void PublishSnapshot(std::string stateArea,
                         protocol::Envelope envelope) override;

    ///  @copydoc IOutboundPublicationSink::PublishEvent
    void PublishEvent(std::string stateArea,
                      protocol::Envelope envelope) override;

    ///  @copydoc IOutboundPublicationSink::PublishRecoverySnapshot
    void PublishRecoverySnapshot(std::string stateArea,
                                 protocol::Envelope envelope,
                                 std::int64_t revision) override;

    ///  @copydoc IOutboundPublicationSink::PublishControl
    void PublishControl(protocol::Envelope envelope) override;

  private:
    ///  Synchronizes access to `attached_`.
    mutable std::mutex mutex_;

    ///  Currently attached session sink, or `nullptr` when none is attached.
    ///  Non-owning.
    IOutboundPublicationSink* attached_ = nullptr;
};

} //  namespace dovahlink::application

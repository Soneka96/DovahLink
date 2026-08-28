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
///  only on this concrete class, not on `IOutboundPublicationSink`: unlike
///  `RevisionTracker`'s template-only methods, nothing about `Attach`/`Detach`
///  is a hard language constraint -- the actual reason is
///  `ai/context/skse/cpp-style.md`'s "a C++ behavior-bearing implementation
///  implements exactly one DovahLink-owned interface." `IOutboundPublicationSink`
///  is this class's one interface, shared with `BoundedOutboundQueue` and the
///  test-only `MockOutboundPublicationSink`/`BlockingPublicationSink`; adding
///  `Attach`/`Detach` to it would wrongly obligate every one of those sinks to
///  implement session-attachment behavior they don't have. So the
///  session-binding caller (`SessionPublicationFactory`) depends on this
///  concrete type instead, the same outcome as `RevisionTracker`'s case but
///  reached for a different reason.
class ActiveSessionPublicationRouter final : public IOutboundPublicationSink {
  public:
    ///  Binds the currently attached sink, replacing any previous binding.
    ///  Does not take ownership; `sink` must outlive either this call's
    ///  matching `Detach()` or a later `Attach()` replacing it. A factory-created
    ///  queue installs an identity-checked teardown callback for this binding.
    ///  @param sink Session-scoped sink to route publications into.
    void Attach(IOutboundPublicationSink& sink);

    ///  Clears the currently attached sink, if any. Publications made after
    ///  this call are dropped until a new sink is attached.
    void Detach();

    ///  Clears the currently attached sink only when it is `expected`. This
    ///  prevents an older session's teardown from detaching a replacement
    ///  session that was attached later. The caller must keep the router alive
    ///  until this operation completes.
    ///  @param expected Sink whose binding is being torn down.
    void Detach(IOutboundPublicationSink& expected);

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

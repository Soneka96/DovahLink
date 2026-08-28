#pragma once

#include "application/active_session_publication_router.hpp"
#include "application/outbound_publication_sink.hpp"
#include "application/publication_diagnostics.hpp"
#include "transport/websocket_session.hpp"

#include <memory>
#include <string>

namespace dovahlink::application {

///  Creates one authenticated session's bounded outbound organization and
///  attaches it to the shared publisher through
///  `ActiveSessionPublicationRouter`. Constructed and injected into the
///  production composition root, but not yet called from any real
///  connection path: the full-duplex session integration that calls this
///  after authentication belongs to a later stage
///  (`ai/context/skse/architecture.md`'s "Production capture and lifecycle
///  composition").
class ISessionPublicationFactory {
  public:
    ///  Allows destruction through the interface.
    virtual ~ISessionPublicationFactory() = default;

    ///  Creates and attaches one session's outbound queue. Attaching
    ///  replaces any previously attached session's binding, per
    ///  `ActiveSessionPublicationRouter::Attach`.
    ///  @param socket Live session socket; must outlive the returned queue.
    ///  @param sessionId Identity of the authenticated session.
    ///  @return The newly created, already-attached queue. The caller owns
    ///  it and must keep it alive for the session's lifetime.
    [[nodiscard]] virtual std::unique_ptr<BoundedOutboundQueue>
    CreateForSession(transport::ISocket& socket, std::string sessionId) = 0;
};

///  @copydoc ISessionPublicationFactory
class SessionPublicationFactory final : public ISessionPublicationFactory {
  public:
    ///  Binds the factory to the router every created queue is attached to
    ///  and the diagnostics sink every created queue reports through.
    ///  @param router Shared publisher's session-binding seam.
    ///  @param diagnostics Diagnostics sink; must outlive every created
    ///  queue.
    SessionPublicationFactory(ActiveSessionPublicationRouter& router,
                              IPublicationDiagnostics& diagnostics);

    ///  @copydoc ISessionPublicationFactory::CreateForSession
    [[nodiscard]] std::unique_ptr<BoundedOutboundQueue>
    CreateForSession(transport::ISocket& socket,
                     std::string sessionId) override;

  private:
    ///  Shared publisher's session-binding seam.
    ActiveSessionPublicationRouter& router_;

    ///  Diagnostics sink every created queue reports through.
    IPublicationDiagnostics& diagnostics_;
};

} //  namespace dovahlink::application

#include "application/session_publication_factory.hpp"

#include <utility>

namespace dovahlink::application {

SessionPublicationFactory::SessionPublicationFactory(
    ActiveSessionPublicationRouter& router, IPublicationDiagnostics& diagnostics)
    : router_(router), diagnostics_(diagnostics) {}

std::unique_ptr<BoundedOutboundQueue>
SessionPublicationFactory::CreateForSession(transport::ISocket& socket,
                                            std::string sessionId) {
    auto queue = std::make_unique<BoundedOutboundQueue>(socket, diagnostics_,
                                                        std::move(sessionId));
    router_.Attach(*queue);
    return queue;
}

} //  namespace dovahlink::application

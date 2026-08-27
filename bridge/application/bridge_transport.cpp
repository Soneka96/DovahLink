#include "application/bridge_transport.hpp"

namespace dovahlink::application {

BridgeTransport::BridgeTransport(transport::ILoopbackListener& listenerV4,
                                 transport::ILoopbackListener& listenerV6)
    : listenerV4_(listenerV4), listenerV6_(listenerV6) {}

void BridgeTransport::Start() {}

void BridgeTransport::CancelCompletions() {}

void BridgeTransport::Close() {
    listenerV4_.Close();
    listenerV6_.Close();
}

} //  namespace dovahlink::application

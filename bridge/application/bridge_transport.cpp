#include "application/bridge_transport.hpp"

#include <boost/system/error_code.hpp>

namespace dovahlink::application {

BridgeTransport::BridgeTransport(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6)
    : listenerV4_(listenerV4), listenerV6_(listenerV6) {}

void BridgeTransport::Start() {}

void BridgeTransport::CancelCompletions() {}

void BridgeTransport::Close() {
    boost::system::error_code ec;
    listenerV4_.Acceptor().close(ec);
    listenerV6_.Acceptor().close(ec);
}

}  // namespace dovahlink::application

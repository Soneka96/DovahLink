#include "application/bridge_transport.hpp"

#include <boost/system/error_code.hpp>

namespace dovahlink::application {

/**
     * @brief Creates a bridge transport for IPv4 and IPv6 loopback listeners.
     *
     * @param listenerV4 IPv4 loopback listener.
     * @param listenerV6 IPv6 loopback listener.
     */
    BridgeTransport::BridgeTransport(transport::LoopbackListener& listenerV4, transport::LoopbackListener& listenerV6)
    : listenerV4_(listenerV4), listenerV6_(listenerV6) {}

/**
 * @brief Starts the bridge transport.
 *
 * This implementation performs no action.
 */
void BridgeTransport::Start() {}

/**
 * @brief Cancels pending transport completions.
 *
 * This implementation performs no action.
 */
void BridgeTransport::CancelCompletions() {}

/**
 * @brief Closes the IPv4 and IPv6 listener acceptors.
 */
void BridgeTransport::Close() {
    boost::system::error_code ec;
    listenerV4_.Acceptor().close(ec);
    listenerV6_.Acceptor().close(ec);
}

}  // namespace dovahlink::application

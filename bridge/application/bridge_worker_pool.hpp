#pragma once

#include "application/active_session_socket.hpp"
#include "application/connection_session.hpp"
#include "application/contained_work.hpp"
#include "application/session.hpp"
#include "shared/scoped_release.hpp"
#include "transport/connection_slot.hpp"
#include "transport/loopback_listener.hpp"

#include <boost/asio/ip/tcp.hpp>

#include <atomic>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <thread>

namespace dovahlink::application {

///  Controls worker startup and shutdown.
class IBridgeWorkerPool {
  public:
    ///  Releases the interface without performing work.
    virtual ~IBridgeWorkerPool() = default;

    ///  Starts worker processing.
    ///  @param workerRunner Containment boundary retained by worker threads.
    virtual void Start(ContainedWorkRunner workerRunner) = 0;

    ///  Stops workers and drains or cancels queued work without joining them.
    virtual void Stop() = 0;

    ///  Waits until every worker has exited.
    virtual void Join() = 0;
};

///  Owns one accept worker per loopback listener and enforces one active client.
///  `Stop()` closes listeners and shuts down the active session socket before
///  `Join()` so blocked accepts, handshakes, and reads can finish. Also the
///  owns the worker lifecycle for one active client.
class BridgeWorkerPool final : public IBridgeWorkerPool {
  public:
    ///  Creates workers for the two loopback listeners.
    ///  @param listenerV4 IPv4 loopback listener owned by the caller.
    ///  @param listenerV6 IPv6 loopback listener owned by the caller.
    ///  @param slot Admission gate enforcing the connected-client limit.
    ///  @param activeSessionSocket Owns active-socket publication and shutdown
    ///      for the connected client.
    ///  @param connectionSession Runs each accepted connection's full session.
    BridgeWorkerPool(transport::ILoopbackListener& listenerV4,
                     transport::ILoopbackListener& listenerV6,
                     transport::IConnectionSlot& slot,
                     IActiveSessionSocket& activeSessionSocket,
                     IConnectionSession& connectionSession);

    ///  Stops and joins any workers that remain active.
    ~BridgeWorkerPool() override;

    ///  Copying a worker pool is not supported.
    BridgeWorkerPool(const BridgeWorkerPool&) = delete;

    ///  Copy assignment is not supported.
    BridgeWorkerPool& operator=(const BridgeWorkerPool&) = delete;

    ///  @copydoc IBridgeWorkerPool::Start
    void Start(ContainedWorkRunner workerRunner) override;

    ///  @copydoc IBridgeWorkerPool::Stop
    void Stop() override;

    ///  @copydoc IBridgeWorkerPool::Join
    void Join() override;

  private:
    ///  Accepts connections from one loopback listener until stopping.
    ///  @param listener Listener whose accept loop is executed.
    ///  @param workerRunner Per-connection exception containment boundary.
    void AcceptLoop(transport::ILoopbackListener& listener,
                    const ContainedWorkRunner& workerRunner);

    ///  Joins the previous connection's session thread, if any is still joinable.
    ///  `ConnectionSlot` admits at most one session at a time, so by the time a
    ///  *new* connection successfully acquires the slot the previous session
    ///  thread has already released it (finished or is finishing) -- this join is
    ///  therefore expected to return immediately, not to reintroduce the
    ///  accept-loop stall `RunSessionOnOwnThread` exists to avoid. Call only while
    ///  holding `connectionThreadMutex_`.
    void JoinConnectionThreadLocked();

    ///  Runs one accepted connection's full session (`IConnectionSession::Run`,
    ///  wrapped in `workerRunner`'s exception containment) on its own thread,
    ///  moving `slotLease` into that thread so the slot is held for the session's
    ///  real lifetime. Letting `AcceptLoop` return to `AcceptLoopbackOnly()`
    ///  immediately after spawning this -- rather than blocking on the session
    ///  itself -- is what lets a second connection attempt arriving while this one
    ///  is still live actually reach `ConnectionSlot::TryAcquire()` and be
    ///  rejected promptly, matching `ai/context/protocol/security.md`'s "reject
    ///  without a WebSocket handshake" contract instead of sitting unrejected in
    ///  the OS accept backlog until this session's own idle timeout frees the
    ///  accept thread.
    ///  @param workerRunner Per-connection exception containment boundary.
    ///  @param connection Identifier assigned to the accepted connection.
    ///  @param socket The accepted, not-yet-upgraded TCP socket.
    ///  @param slotLease The connection slot lease admitting this connection;
    ///  released when the
    ///      session ends.
    void RunSessionOnOwnThread(const ContainedWorkRunner& workerRunner,
                               ConnectionId connection,
                               boost::asio::ip::tcp::socket socket,
                               shared::ScopedRelease slotLease);

    ///  IPv4 listener used by one accept worker.
    transport::ILoopbackListener& listenerV4_;

    ///  IPv6 listener used by one accept worker.
    transport::ILoopbackListener& listenerV6_;

    ///  Admission gate for the active connection.
    transport::IConnectionSlot& slot_;

    ///  Owns active-socket publication, shutdown, and administrative invalidation.
    IActiveSessionSocket& activeSessionSocket_;

    ///  Runs each accepted connection's full session.
    IConnectionSession& connectionSession_;

    ///  Signals both accept workers to stop.
    std::atomic<bool> stopping_{false};

    ///  Next transport connection identifier.
    std::atomic<ConnectionId> nextConnectionId_{1};

    ///  IPv4 accept worker.
    std::thread threadV4_;

    ///  IPv6 accept worker.
    std::thread threadV6_;

    ///  Serializes replacing `connectionThread_` (both accept-loop threads may
    ///  spawn one).
    std::mutex connectionThreadMutex_;

    ///  Runs the most recently accepted connection's full session.
    ///  `ConnectionSlot`'s exclusivity means at most one of these is ever doing
    ///  meaningful work at a time -- see `JoinConnectionThreadLocked`'s own doc
    ///  comment for why a single field suffices here.
    std::thread connectionThread_;
};

} //  namespace dovahlink::application

#pragma once

#include "capture/adapter_capture_handoff_queue.hpp"
#include "dispatch/adapter_native_dispatcher.hpp"
#include "identity/adapter_instance_id.hpp"
#include "ipc/adapter_ipc_peer_proof_provider.hpp"
#include "ipc/ipc_message.hpp"
#include "runtime/adapter_task_marshaller.hpp"

#include <atomic>
#include <cstdint>
#include <mutex>

namespace dovahlink::adapter::ipc {

class IAdapterIpcConnection;

///  The adapter-side private IPC protocol decisions: builds Hello, tracks
///  handshake acceptance, and routes every host-directed request through the
///  same generic pipe -- marshal onto the game thread, translate via
///  `IAdapterNativeDispatcher`, hand the owned result to
///  `IAdapterCaptureHandoffQueue`. No per-message-kind service exists; a
///  resynchronization request is just another marshaled game-thread task
///  that always reports success today, since no real baseline domain is
///  registered yet (this phase's non-goal against speculative domain
///  registries -- the fresh baseline data itself is a later concept's
///  contract, per `IpcResynchronizeResultMessage`'s own documentation). Owns
///  no transport I/O of its own; every lifecycle event reaches this session
///  through `AdapterIpcConnection`'s callbacks.
class IAdapterIpcSession {
public:
  virtual ~IAdapterIpcSession() = default;

  ///  Assigns the connection this session sends messages through. A narrow
  ///  lifecycle-inversion exception for the genuine construction cycle
  ///  between session and connection, per `ai/context/common.md`'s
  ///  "Behavioral boundaries and test isolation": the composition root must
  ///  call this exactly once, before starting the connection.
  virtual void AttachConnection(IAdapterIpcConnection &connection) = 0;

  ///  Builds this session's Hello message.
  [[nodiscard]] virtual IpcMessage PrepareHello() = 0;

  ///  Handles a successful connect: sends Hello through the attached
  ///  connection.
  virtual void HandleConnected() = 0;

  ///  Handles one successfully decoded inbound message.
  ///  @return `true` to keep serving the connection; `false` to end it (a
  ///  received `IpcCloseMessage`, or an unexpected message kind).
  virtual bool HandleMessage(const IpcMessage &message) = 0;

  ///  Handles an inbound frame that could not be decoded: sends a
  ///  best-effort `IpcCloseMessage` (reason `kError`) through the attached
  ///  connection before the connection ends.
  virtual void HandleDecodeFailure() = 0;

  ///  Handles the connection ending, for any reason.
  virtual void HandleDisconnected() = 0;

  ///  Whether the host is currently available: the handshake has been
  ///  accepted and the connection has not since ended.
  [[nodiscard]] virtual bool IsHostAvailable() const = 0;
};

///  @copydoc IAdapterIpcSession
class AdapterIpcSession final : public IAdapterIpcSession {
public:
  ///  Creates a session for the adapter's single, process-lifetime private
  ///  IPC connection.
  ///  @param instanceId This adapter process's own instance identity.
  ///  @param peerProofProvider Supplies this adapter's Hello proof token.
  ///  @param taskMarshaller Marshals capture work onto the Skyrim game
  ///  thread.
  ///  @param dispatcher Performs the one generic key-to-Skyrim translation.
  ///  @param captureQueue Receives owned captured values for handoff.
  AdapterIpcSession(identity::AdapterInstanceId instanceId,
                    IAdapterIpcPeerProofProvider &peerProofProvider,
                    runtime::IAdapterTaskMarshaller &taskMarshaller,
                    dispatch::IAdapterNativeDispatcher &dispatcher,
                    capture::IAdapterCaptureHandoffQueue &captureQueue);

  ///  @copydoc IAdapterIpcSession::AttachConnection
  void AttachConnection(IAdapterIpcConnection &connection) override;

  ///  @copydoc IAdapterIpcSession::PrepareHello
  [[nodiscard]] IpcMessage PrepareHello() override;

  ///  @copydoc IAdapterIpcSession::HandleConnected
  void HandleConnected() override;

  ///  @copydoc IAdapterIpcSession::HandleMessage
  bool HandleMessage(const IpcMessage &message) override;

  ///  @copydoc IAdapterIpcSession::HandleDecodeFailure
  void HandleDecodeFailure() override;

  ///  @copydoc IAdapterIpcSession::HandleDisconnected
  void HandleDisconnected() override;

  ///  @copydoc IAdapterIpcSession::IsHostAvailable
  [[nodiscard]] bool IsHostAvailable() const override;

private:
  ///  Marshals a trivial success capture onto the game thread and replies
  ///  with a matching `IpcResynchronizeResultMessage`.
  void
  HandleResynchronizeRequest(const IpcResynchronizeRequestMessage &request);

  ///  Marshals the dispatcher's translation for `listenEvent.eventKey` onto
  ///  the game thread and hands any captured value to the capture queue.
  void HandleListenEvent(const IpcListenEventMessage &listenEvent);

  ///  Marshals the dispatcher's translation for `readSample.sampleToken`
  ///  onto the game thread and hands any captured value to the capture
  ///  queue.
  void HandleReadSample(const IpcReadSampleMessage &readSample);

  ///  Issues the next monotonic outbound correlation id, starting at 1.
  std::uint64_t NextCorrelationId();

  ///  This adapter process's own instance identity.
  identity::AdapterInstanceId instanceId_;
  ///  Supplies this adapter's Hello proof token.
  IAdapterIpcPeerProofProvider &peerProofProvider_;
  ///  Marshals capture work onto the Skyrim game thread.
  runtime::IAdapterTaskMarshaller &taskMarshaller_;
  ///  Performs the one generic key-to-Skyrim translation.
  dispatch::IAdapterNativeDispatcher &dispatcher_;
  ///  Receives owned captured values for handoff.
  capture::IAdapterCaptureHandoffQueue &captureQueue_;
  ///  The connection this session sends messages through, set once by
  ///  `AttachConnection`. Non-owning: the composition root owns both this
  ///  session and the connection it attaches, for the same plugin lifetime.
  IAdapterIpcConnection *connection_ = nullptr;
  ///  The most recently issued outbound correlation id.
  std::atomic<std::uint64_t> nextCorrelationId_{0};
  ///  Guards `available_`.
  mutable std::mutex availableMutex_;
  ///  Whether the host is currently available.
  bool available_ = false;
};

} //  namespace dovahlink::adapter::ipc

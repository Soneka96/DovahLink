#pragma once

namespace dovahlink::adapter::ipc {

///  Owns the cancellation flag for exactly one admitted, cancellable deferred
///  game-thread dispatch.
///  `AdapterIpcSession::RegisterCancellableDispatchLocked` creates one instance
///  per admitted dispatch, and that dispatch's own task captures it directly,
///  so a task's outcome always depends on the exact object it was given at
///  admission time -- never on a later correlation-id lookup that a differently
///  admitted dispatch could have since replaced. Guarded entirely by
///  `AdapterIpcSession::availableMutex_`; every read and write happens while
///  that lock is held.
struct PendingDispatchCancellationState {
  ///  Set once an `IpcCancelMessage` has marked this exact dispatch cancelled.
  bool cancelled = false;
};

} //  namespace dovahlink::adapter::ipc

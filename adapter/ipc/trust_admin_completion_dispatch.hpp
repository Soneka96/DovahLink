#pragma once

#include "ipc/trust_admin_request_result.hpp"
#include "runtime/adapter_task_marshaller.hpp"

#include <functional>

namespace dovahlink::adapter::ipc {

///  Delivers one trust-admin request's terminal `TrustAdminRequestResult` to
///  `onResult` on the Skyrim game thread, exactly once, regardless of which
///  thread resolved the request (the private IPC read thread, a request's
///  own timeout worker, a connection-lifecycle thread, or the thread that
///  submitted the request). Every trust-admin resolution path in
///  `AdapterIpcSession` -- a completed host result, a timeout, a send
///  failure, an immediate unavailable/at-capacity rejection, an abandoned
///  request on connection close/disconnect, and a destructor's forced
///  abandonment -- routes through this function so `RespondLatent`'s
///  `IVirtualMachine::ReturnLatentResult` call always runs on the one thread
///  Skyrim's scripting VM supports.
///
///  Deliberately independent of `AdapterIpcSession`'s own lifetime: the
///  queued task captures only `onResult` and `result` by value, never
///  `this`, a session lifetime token, or any session-owned mutex, so a
///  request queued before the owning session is destroyed still resumes its
///  latent Papyrus script afterward. Also independent of
///  `AdapterIpcSession`'s bounded, lossy `ScheduleGameThreadDispatch`/
///  `kMaxPendingGameThreadDispatches` mechanism, for the same reason a latent
///  script's terminal completion is not optional, best-effort game-thread
///  work: it must never be dropped merely because that unrelated bound is
///  currently saturated.
///
///  Delivers through `runtime::RunOnGameThreadOrReportFailure`, which owns
///  the actual scheduling, exception containment, and dispatch-failure
///  reporting mechanism -- see its own documentation for why `onResult` is
///  never invoked on the calling thread as a fallback when `marshaller`
///  itself fails to enqueue it.
///  @param marshaller Schedules the completion onto the game thread. Read
///  only at this call, not captured into the queued task.
///  @param onResult The trust-admin request's terminal callback. Invoked
///  exactly once, on the game thread, with `result` moved in -- unless
///  `marshaller` fails to enqueue, in which case it is never invoked.
///  @param result The outcome to deliver.
///  @param onDispatchFailed Invoked, on the calling thread, only if
///  `marshaller.RunOnGameThread` throws.
void DispatchTrustAdminCompletion(
    runtime::IAdapterTaskMarshaller &marshaller,
    std::function<void(TrustAdminRequestResult)> onResult,
    TrustAdminRequestResult result, std::function<void()> onDispatchFailed);

} //  namespace dovahlink::adapter::ipc

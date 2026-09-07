#pragma once

#include "runtime/adapter_task_marshaller.hpp"

#include <functional>

namespace dovahlink::adapter::runtime {

///  Runs `task` on the game thread through `marshaller`, containing any
///  exception `task` itself throws so it can never escape the game thread's
///  own task queue. If `marshaller.RunOnGameThread` itself fails to accept
///  `task` (an exception escapes it before `task` is ever queued), `task`
///  never runs -- running it here, on the calling thread, as a fallback
///  would defeat the reason this function exists -- and `onDispatchFailed`
///  is invoked instead, on the calling thread, so the caller can report the
///  failure. `marshaller` failing to accept a task at all reflects the
///  underlying SKSE task interface itself being unable to function, a
///  condition with no safe recovery short of leaving `task`'s own intended
///  effect undone.
///  @param marshaller Schedules `task` onto the game thread. Read only at
///  this call, not captured into any queued task.
///  @param task The work to run on the game thread, exactly once, unless
///  `marshaller` fails to enqueue it.
///  @param onDispatchFailed Invoked, on the calling thread, only if
///  `marshaller.RunOnGameThread` throws.
void RunOnGameThreadOrReportFailure(IAdapterTaskMarshaller &marshaller,
                                    std::function<void()> task,
                                    std::function<void()> onDispatchFailed);

} //  namespace dovahlink::adapter::runtime

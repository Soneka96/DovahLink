#include "ipc/trust_admin_completion_dispatch.hpp"

#include <utility>

namespace dovahlink::adapter::ipc {

void DispatchTrustAdminCompletion(
    runtime::IAdapterTaskMarshaller &marshaller,
    std::function<void(TrustAdminRequestResult)> onResult,
    TrustAdminRequestResult result, std::function<void()> onDispatchFailed) {
  try {
    marshaller.RunOnGameThread(
        [onResult = std::move(onResult), result = std::move(result)]() mutable {
          try {
            onResult(std::move(result));
          } catch (...) {
            //  Contained: this runs on the game thread via SKSE's task
            //  interface, which must never observe an exception escaping a
            //  queued task.
          }
        });
  } catch (...) {
    //  RunOnGameThread itself failed to enqueue the task before it was ever
    //  queued: onResult was never called and never will be for this result,
    //  so the latent script remains suspended. See this function's own
    //  documentation for why silently running onResult here, on the wrong
    //  thread, is not an acceptable fallback.
    try {
      onDispatchFailed();
    } catch (...) {
      //  Contained: onDispatchFailed runs on whichever thread resolved this
      //  trust-admin request, which must never observe an exception
      //  escaping this reporting call either.
    }
  }
}

} //  namespace dovahlink::adapter::ipc

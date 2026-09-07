#include "ipc/trust_admin_completion_dispatch.hpp"

#include "runtime/game_thread_completion.hpp"

#include <utility>

namespace dovahlink::adapter::ipc {

void DispatchTrustAdminCompletion(
    runtime::IAdapterTaskMarshaller &marshaller,
    std::function<void(TrustAdminRequestResult)> onResult,
    TrustAdminRequestResult result, std::function<void()> onDispatchFailed) {
  runtime::RunOnGameThreadOrReportFailure(
      marshaller,
      [onResult = std::move(onResult), result = std::move(result)]() mutable {
        onResult(std::move(result));
      },
      std::move(onDispatchFailed));
}

} //  namespace dovahlink::adapter::ipc

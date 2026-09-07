#include "runtime/game_thread_completion.hpp"

#include <utility>

namespace dovahlink::adapter::runtime {

void RunOnGameThreadOrReportFailure(IAdapterTaskMarshaller &marshaller,
                                    std::function<void()> task,
                                    std::function<void()> onDispatchFailed) {
  try {
    marshaller.RunOnGameThread([task = std::move(task)]() mutable {
      try {
        task();
      } catch (...) {
        //  Contained: this runs on the game thread via SKSE's own task
        //  interface, which must never observe an exception escaping a
        //  queued task.
      }
    });
  } catch (...) {
    //  RunOnGameThread itself failed to enqueue task before it was ever
    //  queued; see this function's own documentation for why running it
    //  here, on the wrong thread, is not an acceptable fallback.
    try {
      onDispatchFailed();
    } catch (...) {
      //  Contained: onDispatchFailed runs on whichever thread called this
      //  function, which must never observe an exception escaping this
      //  reporting call either.
    }
  }
}

} //  namespace dovahlink::adapter::runtime

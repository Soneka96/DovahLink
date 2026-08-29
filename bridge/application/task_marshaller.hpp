#pragma once

#include <functional>

namespace dovahlink::application {

///  Marshals a callable onto the Skyrim game thread through SKSE's own
///  supported task-interface mechanism, per `ai/context/skse/architecture.md`'s
///  "Production capture and lifecycle composition": no new engine hook, memory
///  patch, or Address Library offset. CommonLib-free so `CadenceTickDriver`
///  stays testable without SKSE; its one concrete implementation
///  (`CommonLibTaskMarshaller`, `bridge/game_state/`) is split into its own
///  CommonLib-dependent file for the same dependency-wall reason documented
///  for `IBridgeCallbackRegistry` in `ai/context/skse/cpp-style.md`.
class ITaskMarshaller {
  public:
    ///  Allows destruction through the interface.
    virtual ~ITaskMarshaller() = default;

    ///  Schedules `task` to run once on the game thread and returns
    ///  immediately; `task` runs asynchronously, at the next point the
    ///  underlying mechanism drains its queue.
    ///  @param task Callable to run on the game thread. Must own every value
    ///  it closes over -- no borrowed Skyrim object or reference may cross
    ///  this boundary.
    virtual void RunOnGameThread(std::function<void()> task) = 0;
};

} //  namespace dovahlink::application

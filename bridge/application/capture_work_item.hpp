#pragma once

#include "application/play_context.hpp"
#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <chrono>
#include <functional>
#include <memory>
#include <optional>
#include <string>

namespace dovahlink::application {

///  One owned, bounded unit of capture work handed from a game-thread
///  callback to `CaptureDispatchWorker`, pinned to the specific `PlayContext`
///  it was captured against (via `IActivePlayContextProvider::CurrentPlayContext`
///  at the moment of capture, not re-read at dispatch time) so a later
///  dispatch can detect that the active context has since changed instead of
///  applying or publishing stale data under the wrong context.
///  `applyAndBuildIfChanged` must own every value it closes over -- no
///  borrowed Skyrim object or reference may cross the worker-thread boundary
///  inside it -- so publication construction (JSON encoding) happens on the
///  worker thread rather than the game thread, per
///  `ai/context/skse/architecture.md`'s "Threading and callbacks".
struct CaptureWorkItem {
    ///  Play context this item was captured against.
    std::shared_ptr<PlayContext> playContext;
    ///  Canonical state-area identifier.
    std::string stateArea;
    ///  Requested delivery mode. The ordering point downgrades `kEvent` to a
    ///  Snapshot publish when `stateArea` has no revision baseline yet
    ///  within `playContext`'s own revision tracker.
    CaptureMode mode;
    ///  Applies the captured value to `playContext` and returns its complete
    ///  post-change state when the value changed, or no value when it did
    ///  not. Called once, on the worker thread, against the pinned
    ///  `playContext`. The returned data must be complete post-change state,
    ///  not a delta -- an Event is itself defined as ordered, complete
    ///  post-change state, the same shape a Snapshot for that area would
    ///  carry -- so the same value may be reused to establish a missing
    ///  Snapshot baseline when `mode` is `kEvent`.
    std::function<std::optional<boost::json::object>(PlayContext&)>
        applyAndBuildIfChanged;
    ///  Wall-clock time the underlying value was captured.
    std::chrono::system_clock::time_point occurredAt;
};

} //  namespace dovahlink::application

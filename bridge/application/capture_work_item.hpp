#pragma once

#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <chrono>
#include <functional>
#include <string>

namespace dovahlink::application {

///  One owned, bounded unit of capture work handed from a game-thread
///  callback to `CaptureDispatchWorker`. `buildData` must own every value it
///  closes over -- no borrowed Skyrim object or reference may cross the
///  worker-thread boundary inside it -- so publication construction (JSON
///  encoding) happens on the worker thread rather than the game thread, per
///  `ai/context/skse/architecture.md`'s "Threading and callbacks".
struct CaptureWorkItem {
    ///  Canonical state-area identifier.
    std::string stateArea;
    ///  Which `IStatePublisher` method this item reaches.
    CaptureMode mode;
    ///  Builds this item's payload; called once, on the worker thread.
    std::function<boost::json::object()> buildData;
    ///  Wall-clock time the underlying value was captured.
    std::chrono::system_clock::time_point occurredAt;
};

} //  namespace dovahlink::application

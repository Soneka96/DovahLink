#include "application/active_play_context_level_sink.hpp"

#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <chrono>
#include <utility>

namespace dovahlink::application {

ActivePlayContextLevelSink::ActivePlayContextLevelSink(
    IPlayContextLifecycle& playContextLifecycle,
    IRegisteredStateAreaPolicy& registeredAreaPolicy,
    ICaptureDispatchWorker& captureWorker, std::string stateArea)
    : playContextLifecycle_(playContextLifecycle),
      registeredAreaPolicy_(registeredAreaPolicy),
      captureWorker_(captureWorker), stateArea_(std::move(stateArea)) {}

void ActivePlayContextLevelSink::OnLevelCaptured(
    std::optional<std::int64_t> level) {
    playContextLifecycle_.CaptureLevel(level);

    if (!registeredAreaPolicy_.IsRegistered(stateArea_)) {
        return;
    }

    //  "capturedValue" is a deliberately generic placeholder field, not
    //  character_level's real wire contract: Phase 4.3 defines that shape.
    //  No caller registers stateArea_ in 4.2, so this branch is unreachable
    //  in production today; it exists so the boundary is provable in tests.
    CaptureWorkItem item{
        .stateArea = stateArea_,
        .mode = CaptureMode::kEvent,
        .buildData =
            [level] {
                boost::json::object data;
                if (level.has_value()) {
                    data["capturedValue"] = *level;
                }
                return data;
            },
        .occurredAt = std::chrono::system_clock::now(),
    };
    (void)captureWorker_.TryEnqueue(std::move(item));
}

bool ActivePlayContextLevelSink::IsCaptureActive() const {
    return playContextLifecycle_.CurrentState() == LifecycleState::kActive;
}

} //  namespace dovahlink::application

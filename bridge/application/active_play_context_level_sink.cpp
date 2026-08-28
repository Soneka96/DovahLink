#include "application/active_play_context_level_sink.hpp"

#include "shared/enums.hpp"

#include <boost/json/object.hpp>

#include <chrono>
#include <utility>

namespace dovahlink::application {

ActivePlayContextLevelSink::ActivePlayContextLevelSink(
    IActivePlayContextProvider& activeContext,
    IRegisteredStateAreaPolicy& registeredAreaPolicy,
    ICaptureDispatchWorker& captureWorker, std::string stateArea)
    : activeContext_(activeContext),
      registeredAreaPolicy_(registeredAreaPolicy),
      captureWorker_(captureWorker), stateArea_(std::move(stateArea)) {}

std::shared_ptr<PlayContext> ActivePlayContextLevelSink::BeginCapture() {
    return activeContext_.CurrentPlayContext();
}

void ActivePlayContextLevelSink::OnLevelCaptured(
    std::shared_ptr<PlayContext> capture, std::optional<std::int64_t> level) {
    if (!capture || !registeredAreaPolicy_.IsRegistered(stateArea_)) {
        return;
    }

    //  "capturedValue" is a deliberately generic placeholder field, not
    //  character_level's real wire contract: Phase 4.3 defines that shape.
    //  No caller registers stateArea_ in 4.2, so this branch is unreachable
    //  in production today; it exists so the boundary is provable in tests.
    CaptureWorkItem item{
        .playContext = std::move(capture),
        .stateArea = stateArea_,
        .mode = CaptureMode::kEvent,
        .applyAndBuildIfChanged =
            [level](PlayContext& context) -> std::optional<boost::json::object> {
            if (!context.characterState.OnLevelCaptured(level)) {
                return std::nullopt;
            }
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

} //  namespace dovahlink::application

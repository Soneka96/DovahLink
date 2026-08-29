#pragma once

#include "application/capture_queue_diagnostics.hpp"

#include <string_view>

namespace dovahlink::game_state {

///  Reports `application::ICaptureQueueDiagnostics` signals through
///  `SKSE::log`. Declared in the CommonLib-linked game-state target because
///  its consumer, `application::CaptureDispatchWorker`, lives in the
///  Skyrim-independent core and cannot itself link SKSE/CommonLib
///  (`application/capture_queue_diagnostics.hpp` documents this split).
class CommonLibCaptureQueueDiagnostics final
    : public application::ICaptureQueueDiagnostics {
  public:
    ///  @copydoc application::ICaptureQueueDiagnostics::RecordCaptureRejected
    void RecordCaptureRejected(std::string_view stateArea,
                               application::CaptureMode mode) noexcept override;
};

} //  namespace dovahlink::game_state

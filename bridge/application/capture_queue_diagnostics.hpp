#pragma once

#include "shared/enums.hpp"

#include <string_view>

namespace dovahlink::application {

///  Receives observability signals from `CaptureDispatchWorker` for its
///  bounded game-thread-to-worker capture queue -- a different queue from
///  the session-facing outbound organization `IPublicationDiagnostics`
///  reports on. Declared here, CommonLib-free, because its consumer
///  (`CaptureDispatchWorker`) lives in the Skyrim-independent core; its one
///  concrete implementation, `CommonLibCaptureQueueDiagnostics`
///  (`game_state/commonlib_capture_queue_diagnostics.hpp`), can only be
///  compiled into a target linked against CommonLibSSE because it logs
///  through `SKSE::log`. This is the same CMake-target dependency-wall split
///  documented in `ai/context/skse/cpp-style.md` for `IBridgeCallbackRegistry`
///  and `IPairingNotificationSink`.
class ICaptureQueueDiagnostics {
  public:
    ///  Allows destruction through the interface.
    virtual ~ICaptureQueueDiagnostics() = default;

    ///  Reports that a capture was rejected because the queue was already at
    ///  capacity. A `kSnapshot` rejection is recoverable -- the next sample
    ///  tick captures current state again -- while a `kEvent` rejection is a
    ///  genuinely lost state transition nothing will re-capture later, so an
    ///  implementation should treat the two differently (for example by
    ///  distinct log severity). Called from the game thread; must never
    ///  block or allocate unboundedly.
    ///  @param stateArea State area the rejected capture belonged to.
    ///  @param mode Delivery mode the rejected capture requested.
    virtual void RecordCaptureRejected(std::string_view stateArea,
                                       CaptureMode mode) noexcept = 0;
};

} //  namespace dovahlink::application

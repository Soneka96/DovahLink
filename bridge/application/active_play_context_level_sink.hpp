#pragma once

#include "application/capture_dispatch_worker.hpp"
#include "application/play_context_lifecycle.hpp"
#include "application/registered_state_area_policy.hpp"

#include <cstdint>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Provides the level-capture capability used by the game-state handler.
class IActivePlayContextLevelSink {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextLevelSink() = default;

    ///  Routes one captured player level, or an unavailable value.
    ///  @param level Captured level, or no value when unavailable.
    virtual void OnLevelCaptured(std::optional<std::int64_t> level) = 0;

    ///  Reports whether a runtime read should be captured right now. A caller
    ///  must check this before reading the runtime value, not only before
    ///  routing the captured result, so no read happens while loading or
    ///  before an authoritative play context exists.
    [[nodiscard]] virtual bool IsCaptureActive() const = 0;
};

///  Routes captured level values into the lifecycle aggregate, which drops
///  captures when no authoritative context exists, and -- only when
///  `stateArea` is registered -- also hands the value to `CaptureDispatchWorker`
///  through the same domain-independent gate sampled capture uses. No caller
///  registers a state area in 4.2, so the worker handoff is currently
///  unreachable in production; it exists so the boundary is real and provable
///  before Phase 4.3 supplies a real domain (`ai/context/skse/architecture.md`'s
///  "Production capture and lifecycle composition").
class ActivePlayContextLevelSink final : public IActivePlayContextLevelSink {
  public:
    ///  Binds the sink to the lifecycle aggregate's write capability, the
    ///  registered-area gate, and the worker the captured value is handed to
    ///  when `stateArea` is registered.
    ///  @param playContextLifecycle Lifecycle aggregate receiving the
    ///  captured level.
    ///  @param registeredAreaPolicy Gate deciding whether `stateArea` is
    ///  currently registered.
    ///  @param captureWorker Receives one owned `CaptureWorkItem` per capture
    ///  when `stateArea` is registered.
    ///  @param stateArea Canonical state-area identifier this sink captures
    ///  for.
    ActivePlayContextLevelSink(IPlayContextLifecycle& playContextLifecycle,
                               IRegisteredStateAreaPolicy& registeredAreaPolicy,
                               ICaptureDispatchWorker& captureWorker,
                               std::string stateArea);

    ///  @copydoc IActivePlayContextLevelSink::OnLevelCaptured
    void OnLevelCaptured(std::optional<std::int64_t> level) override;

    ///  @copydoc IActivePlayContextLevelSink::IsCaptureActive
    [[nodiscard]] bool IsCaptureActive() const override;

  private:
    ///  Lifecycle aggregate receiving the captured level.
    IPlayContextLifecycle& playContextLifecycle_;

    ///  Gate deciding whether `stateArea_` is currently registered.
    IRegisteredStateAreaPolicy& registeredAreaPolicy_;

    ///  Receives one owned `CaptureWorkItem` per capture when `stateArea_` is
    ///  registered.
    ICaptureDispatchWorker& captureWorker_;

    ///  Canonical state-area identifier this sink captures for.
    std::string stateArea_;
};

} //  namespace dovahlink::application

#pragma once

#include "application/active_play_context_provider.hpp"
#include "application/capture_dispatch_worker.hpp"
#include "application/play_context.hpp"
#include "application/registered_state_area_policy.hpp"

#include <cstdint>
#include <memory>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Provides the level-capture capability used by the game-state handler.
class IActivePlayContextLevelSink {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextLevelSink() = default;

    ///  Returns the play context this capture should target, or `nullptr`
    ///  when no capture should occur right now (loading, or no active
    ///  context). Callers must obtain this immediately before performing the
    ///  runtime read the capture depends on, and pass the exact same
    ///  context to `OnLevelCaptured`, so the active-context guard and the
    ///  context a captured value is pinned against come from one atomic
    ///  snapshot instead of two separate lifecycle reads that could observe
    ///  different play contexts.
    [[nodiscard]] virtual std::shared_ptr<PlayContext> BeginCapture() = 0;

    ///  Routes one captured player level, or an unavailable value, into the
    ///  play context `capture` was pinned against by a prior `BeginCapture`
    ///  call for this same capture attempt.
    ///  @param capture Context returned by `BeginCapture` for this capture
    ///  attempt.
    ///  @param level Captured level, or no value when unavailable.
    virtual void OnLevelCaptured(std::shared_ptr<PlayContext> capture,
                                 std::optional<std::int64_t> level) = 0;
};

///  Pins the currently active play context through `BeginCapture`, then --
///  only when `stateArea` is registered -- hands the captured value to
///  `CaptureDispatchWorker` as an owned `CaptureWorkItem` targeting that
///  pinned context, through the same domain-independent gate sampled capture
///  uses. No caller registers a state area in 4.2, so the worker handoff is
///  currently unreachable in production; it exists so the boundary is real
///  and provable before Phase 4.3 supplies a real domain
///  (`ai/context/skse/architecture.md`'s "Production capture and lifecycle
///  composition").
class ActivePlayContextLevelSink final : public IActivePlayContextLevelSink {
  public:
    ///  Binds the sink to the pinned-context provider, the registered-area
    ///  gate, and the worker the captured value is handed to when
    ///  `stateArea` is registered.
    ///  @param activeContext Provides the play context a capture attempt
    ///  should target.
    ///  @param registeredAreaPolicy Gate deciding whether `stateArea` is
    ///  currently registered.
    ///  @param captureWorker Receives one owned `CaptureWorkItem` per capture
    ///  when `stateArea` is registered.
    ///  @param stateArea Canonical state-area identifier this sink captures
    ///  for.
    ActivePlayContextLevelSink(IActivePlayContextProvider& activeContext,
                               IRegisteredStateAreaPolicy& registeredAreaPolicy,
                               ICaptureDispatchWorker& captureWorker,
                               std::string stateArea);

    ///  @copydoc IActivePlayContextLevelSink::BeginCapture
    [[nodiscard]] std::shared_ptr<PlayContext> BeginCapture() override;

    ///  @copydoc IActivePlayContextLevelSink::OnLevelCaptured
    void OnLevelCaptured(std::shared_ptr<PlayContext> capture,
                         std::optional<std::int64_t> level) override;

  private:
    ///  Provides the play context a capture attempt should target.
    IActivePlayContextProvider& activeContext_;

    ///  Gate deciding whether `stateArea_` is currently registered.
    IRegisteredStateAreaPolicy& registeredAreaPolicy_;

    ///  Receives one owned `CaptureWorkItem` per capture when `stateArea_` is
    ///  registered.
    ICaptureDispatchWorker& captureWorker_;

    ///  Canonical state-area identifier this sink captures for.
    std::string stateArea_;
};

} //  namespace dovahlink::application

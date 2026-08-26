#pragma once

#include "application/coordinator.hpp"
#include "application/play_context.hpp"
#include "application/play_context_transition.hpp"
#include "shared/enums.hpp"

#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <string>

namespace dovahlink::application {

///  Generates a fresh opaque play-context identifier, or no value on failure.
using PlayContextLifecycleIdGenerator =
    std::function<std::optional<std::string>()>;

///  Creates one empty play context for a freshly generated identifier.
using PlayContextFactory =
    std::function<std::shared_ptr<PlayContext>(std::string)>;

///  Decodes SKSE's post-load success signal without fabricating success for a
///  null payload.
[[nodiscard]] bool DecodePostLoadGameSuccess(const void* rawData);

///  Submits one lifecycle callback through its containment boundary.
///  Missing, rejecting, or throwing runners return `false` without allowing an
///  exception to escape the runtime-facing caller.
[[nodiscard]] bool RunContainedLifecycleWork(
    const ContainedWorkRunner& callbackRunner, ContainedWork work) noexcept;

///  Owns the lifecycle state, identity, and authoritative state for one
///  currently loaded Skyrim play context.
class IPlayContextLifecycle {
  public:
    ///  Allows destruction through the interface.
    virtual ~IPlayContextLifecycle() = default;

    ///  Processes one lifecycle signal and atomically updates lifecycle state and
    ///  the published play context.
    ///  @param event Signal received from the runtime adapter.
    ///  @return The resulting invalidation/creation effect.
    virtual PlayContextTransition HandleEvent(LifecycleEvent event) = 0;

    ///  Returns the identifier of the currently active play context.
    [[nodiscard]] virtual std::optional<std::string>
    CurrentPlayContextId() const = 0;

    ///  Returns the lifecycle state of the aggregate.
    [[nodiscard]] virtual LifecycleState CurrentState() const = 0;

    ///  Captures one level value into the currently active play context, or
    ///  drops it when no authoritative context exists.
    ///  @param level Captured level, or no value when unavailable.
    virtual void CaptureLevel(std::optional<std::int64_t> level) = 0;
};

///  Keeps lifecycle state and its published play context as one synchronized
///  aggregate. A failed context construction leaves the whole aggregate at its
///  previous consistent state.
class PlayContextLifecycle final : public IPlayContextLifecycle {
  public:
    ///  Creates an aggregate starting in `kNoContext`.
    ///  @param generateId Generates a fresh play-context identifier.
    ///  @param createContext Creates the authoritative state container for a new
    ///      identifier.
    explicit PlayContextLifecycle(
        PlayContextLifecycleIdGenerator generateId = DefaultGenerator(),
        PlayContextFactory createContext = DefaultFactory());

    ///  @copydoc IPlayContextLifecycle::HandleEvent
    PlayContextTransition HandleEvent(LifecycleEvent event) override;

    ///  @copydoc IPlayContextLifecycle::CurrentPlayContextId
    [[nodiscard]] std::optional<std::string>
    CurrentPlayContextId() const override;

    ///  @copydoc IPlayContextLifecycle::CurrentState
    [[nodiscard]] LifecycleState CurrentState() const override;

    ///  @copydoc IPlayContextLifecycle::CaptureLevel
    void CaptureLevel(std::optional<std::int64_t> level) override;

  private:
    ///  Returns the default CSPRNG-backed identifier generator.
    static PlayContextLifecycleIdGenerator DefaultGenerator();

    ///  Returns the default play-context factory.
    static PlayContextFactory DefaultFactory();

    ///  Invalidates the aggregate while `mutex_` is held.
    PlayContextTransition InvalidateLocked();

    ///  Activates a new context while `mutex_` is held.
    PlayContextTransition ActivateLocked();

    ///  Produces fresh play-context identifiers.
    PlayContextLifecycleIdGenerator generateId_;

    ///  Creates the state container for a new play context.
    PlayContextFactory createContext_;

    ///  Synchronizes every lifecycle and context-state operation.
    mutable std::mutex mutex_;

    ///  Current lifecycle state.
    LifecycleState state_ = LifecycleState::kNoContext;

    ///  Identifier of the active play context, when one is present.
    std::optional<std::string> currentPlayContextId_;

    ///  Authoritative state for the current play context, when one is present.
    std::shared_ptr<PlayContext> current_;
};

} //  namespace dovahlink::application

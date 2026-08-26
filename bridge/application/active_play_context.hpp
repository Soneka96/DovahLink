#pragma once

#include "application/game_lifecycle_tracker.hpp"
#include "application/play_context.hpp"

#include <memory>
#include <mutex>

namespace dovahlink::application {

///  Owns and exposes the active play-context lifecycle capability.
class IActivePlayContext {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContext() = default;

    ///  Returns the currently active play context.
    [[nodiscard]] virtual std::shared_ptr<PlayContext>
    AcquireCurrent() const = 0;

    ///  Clears the active play context.
    virtual void Reset() = 0;

    ///  Replaces the active play context with a freshly created one.
    ///  @param id Opaque identifier for the new context.
    ///  @return The newly active context.
    virtual std::shared_ptr<PlayContext> Begin(std::string id) = 0;

    ///  Atomically replaces the active play context with a freshly created one.
    ///  The new context is constructed before the active handle is swapped, so
    ///  readers never observe an empty context during replacement.
    ///  @param id Opaque identifier for the replacement context.
    ///  @return The newly active context.
    virtual std::shared_ptr<PlayContext> Replace(std::string id) = 0;
};

///  Owns the currently active `PlayContext`, if any, and hands out shared
///  ownership so an in-flight handler cannot be left holding a dangling
///  reference when a lifecycle callback invalidates or replaces the context.
class ActivePlayContext final : public IActivePlayContext {
  public:
    ///  @copydoc IActivePlayContext::AcquireCurrent
    [[nodiscard]] std::shared_ptr<PlayContext>
    AcquireCurrent() const override;

    ///  @copydoc IActivePlayContext::Reset
    void Reset() override;

    ///  @copydoc IActivePlayContext::Begin
    std::shared_ptr<PlayContext> Begin(std::string id) override;

    ///  @copydoc IActivePlayContext::Replace
    std::shared_ptr<PlayContext> Replace(std::string id) override;

  private:
    ///  Synchronizes access to `current_`.
    mutable std::mutex mutex_;

    ///  Currently active play context, or `nullptr` outside an active context.
    std::shared_ptr<PlayContext> current_;
};

///  Applies one `GameLifecycleTracker` transition to an `IActivePlayContext`.
///  Invalidation resets it, and a freshly minted identifier atomically replaces
///  the previous one.
///  @param activePlayContext Context ownership updated by this transition.
///  @param transition Effect produced by `GameLifecycleTracker::HandleEvent`.
void ApplyLifecycleTransition(
    IActivePlayContext& activePlayContext,
    const GameLifecycleTracker::Transition& transition);

} //  namespace dovahlink::application

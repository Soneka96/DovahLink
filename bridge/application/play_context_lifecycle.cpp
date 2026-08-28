#include "application/play_context_lifecycle.hpp"

#include "security/csprng.hpp"

#include <stdexcept>
#include <utility>

namespace dovahlink::application {

bool DecodePostLoadGameSuccess(const void* rawData) {
    return rawData != nullptr;
}

bool RunContainedLifecycleWork(const ContainedWorkRunner& callbackRunner,
                               ContainedWork work) noexcept {
    if (!callbackRunner) {
        return false;
    }
    try {
        return callbackRunner(std::move(work));
    } catch (...) {
        return false;
    }
}

PlayContextLifecycleIdGenerator PlayContextLifecycle::DefaultGenerator() {
    return &security::GenerateOpaqueId;
}

PlayContextFactory PlayContextLifecycle::DefaultFactory() {
    return [](std::string id) {
        return std::make_shared<PlayContext>(std::move(id));
    };
}

PlayContextLifecycle::PlayContextLifecycle(
    PlayContextLifecycleIdGenerator generateId,
    PlayContextFactory createContext)
    : generateId_(std::move(generateId)),
      createContext_(std::move(createContext)) {}

PlayContextTransition PlayContextLifecycle::InvalidateLocked() {
    const bool hadContext = state_ != LifecycleState::kNoContext;
    state_ = LifecycleState::kNoContext;
    currentPlayContextId_.reset();
    current_.reset();
    return PlayContextTransition{.contextInvalidated = hadContext};
}

PlayContextTransition PlayContextLifecycle::ActivateLocked() {
    const bool hadActiveContext = state_ == LifecycleState::kActive;
    std::optional<std::string> id = generateId_ ? generateId_() : std::nullopt;
    if (!id.has_value()) {
        state_ = LifecycleState::kNoContext;
        currentPlayContextId_.reset();
        current_.reset();
        return PlayContextTransition{.contextInvalidated = hadActiveContext};
    }

    if (!createContext_) {
        throw std::runtime_error("play-context factory is unavailable");
    }
    auto context = createContext_(*id);
    if (!context) {
        throw std::runtime_error("play-context factory returned no context");
    }

    //  All potentially-throwing work is complete before any aggregate member is
    //  changed. The remaining moves and scalar assignment preserve the previous
    //  consistent state if context creation fails.
    PlayContextTransition transition{.contextInvalidated = hadActiveContext,
                                     .newPlayContextId = id};
    state_ = LifecycleState::kActive;
    currentPlayContextId_ = std::move(id);
    current_ = std::move(context);
    return transition;
}

PlayContextTransition PlayContextLifecycle::HandleEvent(
    LifecycleEvent event) {
    std::lock_guard<std::mutex> lock(mutex_);
    switch (event) {
    case LifecycleEvent::kRevert:
        return InvalidateLocked();

    case LifecycleEvent::kPreLoadGame: {
        const bool hadActiveContext = state_ == LifecycleState::kActive;
        state_ = LifecycleState::kLoading;
        currentPlayContextId_.reset();
        current_.reset();
        return PlayContextTransition{.contextInvalidated = hadActiveContext};
    }

    case LifecycleEvent::kPostLoadGameSuccess:
    case LifecycleEvent::kNewGame:
        return ActivateLocked();

    case LifecycleEvent::kPostLoadGameFailure: {
        const bool hadActiveContext = state_ == LifecycleState::kActive;
        state_ = LifecycleState::kNoContext;
        currentPlayContextId_.reset();
        current_.reset();
        return PlayContextTransition{.contextInvalidated = hadActiveContext};
    }
    }
    return PlayContextTransition{};
}

std::optional<std::string>
PlayContextLifecycle::CurrentPlayContextId() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return currentPlayContextId_;
}

LifecycleState PlayContextLifecycle::CurrentState() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return state_;
}

std::shared_ptr<PlayContext> PlayContextLifecycle::CurrentPlayContext() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return current_;
}

} //  namespace dovahlink::application

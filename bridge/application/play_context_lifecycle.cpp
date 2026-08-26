#include "application/play_context_lifecycle.hpp"

#include "security/csprng.hpp"

#include <stdexcept>
#include <utility>

namespace dovahlink::application {

bool DecodePostLoadGameSuccess(const void* rawData) {
    return rawData != nullptr;
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

IPlayContextLifecycle::Transition PlayContextLifecycle::InvalidateLocked() {
    const bool hadContext = state_ != LifecycleState::kNoContext;
    state_ = LifecycleState::kNoContext;
    currentPlayContextId_.reset();
    current_.reset();
    return Transition{.contextInvalidated = hadContext};
}

IPlayContextLifecycle::Transition PlayContextLifecycle::ActivateLocked() {
    const bool hadActiveContext = state_ == LifecycleState::kActive;
    std::optional<std::string> id = generateId_ ? generateId_() : std::nullopt;
    if (!id.has_value()) {
        state_ = LifecycleState::kActive;
        currentPlayContextId_.reset();
        current_.reset();
        return Transition{.contextInvalidated = hadActiveContext};
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
    Transition transition{.contextInvalidated = hadActiveContext,
                          .newPlayContextId = id};
    state_ = LifecycleState::kActive;
    currentPlayContextId_ = std::move(id);
    current_ = std::move(context);
    return transition;
}

IPlayContextLifecycle::Transition PlayContextLifecycle::HandleEvent(
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
        return Transition{.contextInvalidated = hadActiveContext};
    }

    case LifecycleEvent::kPostLoadGameSuccess:
    case LifecycleEvent::kNewGame:
        return ActivateLocked();

    case LifecycleEvent::kPostLoadGameFailure: {
        const bool hadActiveContext = state_ == LifecycleState::kActive;
        state_ = LifecycleState::kNoContext;
        currentPlayContextId_.reset();
        current_.reset();
        return Transition{.contextInvalidated = hadActiveContext};
    }
    }
    return Transition{};
}

std::shared_ptr<PlayContext> PlayContextLifecycle::AcquireCurrent() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return current_;
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

} //  namespace dovahlink::application

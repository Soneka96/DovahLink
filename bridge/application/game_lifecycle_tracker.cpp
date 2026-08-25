#include "application/game_lifecycle_tracker.hpp"

#include "security/csprng.hpp"

#include <utility>

namespace dovahlink::application {

bool DecodePostLoadGameSuccess(const void *rawData) {
  return rawData != nullptr;
}

PlayContextIdGenerator GameLifecycleTracker::DefaultGenerator() {
  return &security::GenerateOpaqueId;
}

GameLifecycleTracker::GameLifecycleTracker(PlayContextIdGenerator generateId)
    : generateId_(std::move(generateId)) {}

GameLifecycleTracker::Transition GameLifecycleTracker::Invalidate() {
  bool hadContext = state_ != LifecycleState::kNoContext;
  state_ = LifecycleState::kNoContext;
  currentPlayContextId_.reset();
  return Transition{.contextInvalidated = hadContext};
}

GameLifecycleTracker::Transition GameLifecycleTracker::Activate() {
  bool hadActiveContext = state_ == LifecycleState::kActive;
  std::optional<std::string> id = generateId_ ? generateId_() : std::nullopt;
  state_ = LifecycleState::kActive;
  currentPlayContextId_ = id;
  return Transition{.contextInvalidated = hadActiveContext,
                    .newPlayContextId = std::move(id)};
}

GameLifecycleTracker::Transition
GameLifecycleTracker::HandleEvent(LifecycleEvent event) {
  switch (event) {
  case LifecycleEvent::kRevert:
    return Invalidate();

  case LifecycleEvent::kPreLoadGame: {
    bool hadActiveContext = state_ == LifecycleState::kActive;
    state_ = LifecycleState::kLoading;
    currentPlayContextId_.reset();
    return Transition{.contextInvalidated = hadActiveContext};
  }

  case LifecycleEvent::kPostLoadGameSuccess:
    return Activate();

  case LifecycleEvent::kPostLoadGameFailure: {
    // The previous context, if any, was already invalidated at
    // kPreLoadGame (or by an intervening Revert); a failed load must
    // not revive it. hadActiveContext only becomes true here for an
    // out-of-order signal that skipped kPreLoadGame entirely.
    bool hadActiveContext = state_ == LifecycleState::kActive;
    state_ = LifecycleState::kNoContext;
    currentPlayContextId_.reset();
    return Transition{.contextInvalidated = hadActiveContext};
  }

  case LifecycleEvent::kNewGame:
    return Activate();
  }
  return Transition{}; // unreachable: every enumerator is handled above
}

std::optional<std::string> GameLifecycleTracker::CurrentPlayContextId() const {
  return currentPlayContextId_;
}

LifecycleState GameLifecycleTracker::CurrentState() const { return state_; }

} // namespace dovahlink::application

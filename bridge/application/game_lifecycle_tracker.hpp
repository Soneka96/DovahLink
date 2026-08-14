#pragma once

#include <functional>
#include <optional>
#include <string>

namespace dovahlink::application {

/// Lifecycle state of the currently tracked Skyrim play context.
enum class LifecycleState {
    /// No play context is active: the main menu, or before any game has loaded.
    kNoContext,
    /// A load or new-game attempt is in progress; no context is active yet.
    kLoading,
    /// A play context is active and current.
    kActive,
};

/// Raw Skyrim/SKSE lifecycle signals recognized by GameLifecycleTracker.
enum class LifecycleEvent {
    /// SKSE's serialization revert callback: unconditional teardown of any
    /// current runtime game state. Fires before every load and new game, not
    /// only a genuine return to the main menu.
    kRevert,
    /// SKSE::MessagingInterface::kPreLoadGame: a save has been selected but
    /// not yet loaded.
    kPreLoadGame,
    /// SKSE::MessagingInterface::kPostLoadGame carrying a `true` success value.
    kPostLoadGameSuccess,
    /// SKSE::MessagingInterface::kPostLoadGame carrying a `false` success value.
    kPostLoadGameFailure,
    /// SKSE::MessagingInterface::kNewGame.
    kNewGame,
};

/// Generates a fresh opaque play-context identifier, or no value on failure.
using PlayContextIdGenerator = std::function<std::optional<std::string>()>;

/// Decodes SKSE's kPostLoadGame success flag. SKSE encodes it directly as
/// the pointer's bit pattern (0 or 1), not as a pointer to a bool in
/// memory -- confirmed from a crash dereferencing address 0x1, i.e.
/// `reinterpret_cast<void*>(true)`. A null value means failure rather than
/// fabricating success for an unexpected payload (ai/context/common.md's
/// "do not hide stale, missing, or incompatible data" quality floor).
[[nodiscard]] bool DecodePostLoadGameSuccess(const void* rawData);

/// Tracks the currently active Skyrim play context from raw SKSE lifecycle
/// signals, independent of any SKSE or CommonLib type.
///
/// The transition table tolerates realistic ordering variation: no
/// transition asserts a specific prior state, and repeated or out-of-order
/// signals are idempotent rather than treated as errors. This reflects that
/// public SKSE/CommonLibSSE-NG documentation does not fully establish the
/// ordering of the serialization revert callback relative to the
/// messaging-interface lifecycle events (see task.md's research notes).
///
/// Not thread-safe: the runtime adapter that owns one instance is
/// responsible for calling it from a single serialized callback boundary,
/// the same responsibility RevisionTracker places on its own caller.
class GameLifecycleTracker {
public:
    /// Reports the effect of one processed lifecycle event.
    struct Transition {
        /// Whether a previously active or loading play context was invalidated.
        bool contextInvalidated = false;
        /// The freshly minted play context identifier, when one was created.
        std::optional<std::string> newPlayContextId;
    };

    /// Creates a tracker starting in `kNoContext`.
    /// @param generateId Generates a fresh play-context identifier; defaults
    ///     to the same CSPRNG-backed generator used for session and message
    ///     identifiers.
    explicit GameLifecycleTracker(PlayContextIdGenerator generateId = DefaultGenerator());

    /// Processes one raw lifecycle signal and updates tracked state.
    /// @param event Signal received from the runtime adapter.
    /// @return The resulting invalidation/creation effect.
    Transition HandleEvent(LifecycleEvent event);

    /// Returns the identifier of the currently active play context.
    /// @return The active identifier, or no value outside `kActive`.
    [[nodiscard]] std::optional<std::string> CurrentPlayContextId() const;

    /// Returns the tracker's current lifecycle state.
    [[nodiscard]] LifecycleState CurrentState() const;

private:
    /// Returns the default identifier generator.
    static PlayContextIdGenerator DefaultGenerator();

    /// Unconditionally invalidates any active or loading context and returns
    /// to `kNoContext`. Idempotent when already in `kNoContext`.
    Transition Invalidate();

    /// Mints a fresh play context identifier and enters `kActive`.
    Transition Activate();

    /// Produces fresh play-context identifiers.
    PlayContextIdGenerator generateId_;

    /// Current lifecycle state.
    LifecycleState state_ = LifecycleState::kNoContext;

    /// Identifier of the active play context, when `state_` is `kActive`.
    std::optional<std::string> currentPlayContextId_;
};

}  // namespace dovahlink::application

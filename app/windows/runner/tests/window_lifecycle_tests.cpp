#include "window_lifecycle_state.h"

#include <cstdlib>
#include <optional>

///  Verifies only one close request can be pending at a time.
///  @return `true` when the first request is admitted and a duplicate is rejected.
bool BeginRejectsDuplicateRequests() {
    WindowLifecycleState state;
    return state.BeginClose(1) && !state.BeginClose(2) &&
           state.HasPendingClose() &&
           state.PendingCloseGeneration() == std::optional<std::uint64_t>(1);
}

///  Verifies stale and repeated completions cannot complete another request.
///  @return `true` when only the active request completes.
bool CompleteRejectsStaleAndRepeatedTokens() {
    WindowLifecycleState state;
    return state.BeginClose(7) && !state.CompleteClose(8) &&
           state.HasPendingClose() && state.CompleteClose(7) &&
           !state.CompleteClose(7) && !state.HasPendingClose();
}

///  Verifies an empty generation cannot create a pending request.
///  @return `true` when generation zero is rejected.
bool BeginRejectsTheEmptyGeneration() {
    WindowLifecycleState state;
    return !state.BeginClose(0) && !state.HasPendingClose();
}

///  Verifies a timeout followed by a late Dart reply closes only once.
///  @return `true` when the late reply is rejected after timeout completion.
bool TimeoutMakesLateDartReplyStale() {
    WindowLifecycleState state;
    return state.BeginClose(9) && state.CompleteClose(9) &&
           !state.CompleteClose(9);
}

///  Verifies the timer-creation fallback can complete a close only once.
///  @return `true` when fallback completion admits one window close.
bool TimerCreationFailureAllowsOneClose() {
    WindowLifecycleState state;
    return state.BeginClose(10) && state.CompleteClose(10) &&
           !state.CompleteClose(10);
}

///  Verifies uncommitted session ending is ignored and committed cleanup starts once.
///  @return `true` when only the first committed session-end event is admitted.
bool SessionEndCleanupRunsOnceAfterCommit() {
    WindowLifecycleState state;
    return !state.BeginSessionEndCleanup(false) &&
           state.BeginSessionEndCleanup(true) &&
           !state.BeginSessionEndCleanup(true);
}

///  Verifies lifecycle admission resets for a recreated native window.
///  @return `true` when the next window may start its own cleanup.
bool ResetStartsFreshWindowLifecycle() {
    WindowLifecycleState state;
    if (!state.BeginSessionEndCleanup(true) || !state.BeginClose(11)) {
        return false;
    }
    state.ResetForWindow();
    return !state.HasPendingClose() &&
           state.BeginSessionEndCleanup(true) && state.BeginClose(12) &&
           !state.CompleteClose(11) && state.HasPendingClose() &&
           state.PendingCloseGeneration() == std::optional<std::uint64_t>(12);
}

///  Runs native window-lifecycle state tests.
///  @return `EXIT_SUCCESS` when every lifecycle condition is satisfied.
int main() {
    if (!BeginRejectsDuplicateRequests() ||
        !CompleteRejectsStaleAndRepeatedTokens() ||
        !BeginRejectsTheEmptyGeneration() ||
        !TimeoutMakesLateDartReplyStale() ||
        !TimerCreationFailureAllowsOneClose() ||
        !SessionEndCleanupRunsOnceAfterCommit() ||
        !ResetStartsFreshWindowLifecycle()) {
        return EXIT_FAILURE;
    }
    return EXIT_SUCCESS;
}

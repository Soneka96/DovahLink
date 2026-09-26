#include "../flutter_window.h"
#include "../window_lifecycle_state.h"

#include <cstdlib>
#include <functional>
#include <memory>
#include <optional>
#include <utility>
#include <vector>

#include <flutter/generated_plugin_registrant.h>

///  Test runner window exposing the native message handler through fake Flutter boundaries.
class TestFlutterWindow final : public FlutterWindow {
  public:
    ///  Creates a test window with real lifecycle state and no Flutter engine.
    ///  @param project A Dart project used only to satisfy the runner constructor.
    explicit TestFlutterWindow(const flutter::DartProject& project)
        : TestFlutterWindow(project, new WindowLifecycleState()) {}

    ///  Whether the fake method channel accepts the close request.
    bool close_request_available = true;

    ///  The fake Dart completion callback captured by [RequestDartClose].
    std::function<void()> close_completion;

    ///  The results returned to successive Flutter window-proc calls.
    std::vector<std::optional<LRESULT>> flutter_results;

    ///  Number of requests sent through the fake lifecycle channel.
    int close_request_count = 0;

    ///  Number of committed-session cleanup requests sent through the fake channel.
    int session_end_request_count = 0;

    ///  Number of close messages delegated to Flutter.
    int flutter_close_message_count = 0;

    ///  Number of all messages delegated to Flutter.
    int flutter_message_count = 0;

    ///  Number of shutdown-specific messages delegated to Flutter.
    int flutter_session_message_count = 0;

    ///  The pending close token, if cleanup is still waiting for Dart or timeout.
    ///  @return The active generation, or `std::nullopt` when no close is pending.
    std::optional<std::uint64_t> PendingCloseGeneration() const {
        return lifecycle_state_->PendingCloseGeneration();
    }

  protected:
    ///  Creates the native window without starting a Flutter engine.
    ///  @return `true` so native messages can target this test object.
    bool OnCreate() override { return true; }

    ///  Captures the production close-completion callback instead of invoking a method channel.
    ///  @param completion The completion posted by a simulated Dart result.
    ///  @return Whether the fake channel accepted the request.
    bool RequestDartClose(std::function<void()> completion) noexcept override {
        ++close_request_count;
        if (!close_request_available) {
            return false;
        }
        close_completion = std::move(completion);
        return true;
    }

    ///  Records committed-session cleanup without starting a Flutter engine.
    ///  @return `true` to model an available method channel.
    bool RequestDartSessionEndCleanup() noexcept override {
        ++session_end_request_count;
        return true;
    }

    ///  Records and returns configured outcomes for messages offered to Flutter.
    ///  @param window The native window receiving the message.
    ///  @param message The Win32 message offered to Flutter.
    ///  @param wparam The message's first native parameter.
    ///  @param lparam The message's second native parameter.
    ///  @return The next configured handling result, or `std::nullopt` when declined.
    std::optional<LRESULT> HandleFlutterWindowProc(
        HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept override {
        (void)window;
        (void)wparam;
        (void)lparam;
        ++flutter_message_count;
        if (message == WM_QUERYENDSESSION || message == WM_ENDSESSION) {
            ++flutter_session_message_count;
        }
        if (message == WM_CLOSE) {
            ++flutter_close_message_count;
            const std::size_t result_index =
                static_cast<std::size_t>(flutter_close_message_count - 1);
            if (result_index < flutter_results.size()) {
                return flutter_results[result_index];
            }
        }
        return std::nullopt;
    }

  private:
    ///  Creates a test window around the state pointer retained for assertions.
    ///  @param project A Dart project used only to satisfy the runner constructor.
    ///  @param lifecycleState State owned by the base runner window.
    TestFlutterWindow(const flutter::DartProject& project,
                      WindowLifecycleState* lifecycleState)
        : FlutterWindow(project,
                        std::unique_ptr<IWindowLifecycleState>(lifecycleState)),
          lifecycle_state_(lifecycleState) {}

    ///  Observes the lifecycle state transferred to [FlutterWindow].
    WindowLifecycleState* lifecycle_state_;
};

///  Supplies the plugin registration symbol required by the linked production runner source.
///  @param registry The Flutter plugin registry, unused by this native lifecycle test.
void RegisterPlugins(flutter::PluginRegistry* registry) { (void)registry; }

///  Creates a real hidden runner window for exercising its WndProc path.
///  @param window The test runner object that owns the native window.
///  @return A valid window handle, or `nullptr` when Windows cannot create it.
HWND CreateTestWindow(TestFlutterWindow& window) {
    const Win32Window::Point origin(0, 0);
    const Win32Window::Size size(1, 1);
    return window.Create(L"window_lifecycle_tests", origin, size)
               ? window.GetHandle()
               : nullptr;
}

///  Removes and dispatches the close-completion message posted by the test callback.
///  @param handle The native window that owns the posted message.
///  @return `true` when the completion message was posted and handled.
bool DispatchCloseCompletion(HWND handle) {
    MSG message{};
    if (!::PeekMessageW(&message, handle, WM_APP + 1, WM_APP + 1, PM_REMOVE)) {
        return false;
    }
    ::DispatchMessageW(&message);
    return true;
}

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
           state.HasPendingClose() && !state.CloseCleanupCompleted() &&
           state.CompleteClose(7) && state.CloseCleanupCompleted() &&
           !state.CompleteClose(7) && !state.BeginClose(8) &&
           !state.HasPendingClose();
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
           state.CloseCleanupCompleted() && !state.CompleteClose(9);
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
    return !state.HasPendingClose() && !state.CloseCleanupCompleted() &&
           state.BeginSessionEndCleanup(true) && state.BeginClose(12) &&
           !state.CompleteClose(11) && state.HasPendingClose() &&
           state.PendingCloseGeneration() == std::optional<std::uint64_t>(12);
}

///  Verifies the actual runner handler delegates a completed close through Flutter once.
///  @return `true` when Dart cleanup runs once and Flutter receives the original and replayed close.
bool CloseCompletionUsesFlutterWindowPipeline() {
    flutter::DartProject project(L"data");
    TestFlutterWindow window(project);
    HWND handle = CreateTestWindow(window);
    if (handle == nullptr) {
        return false;
    }
    window.flutter_results = {LRESULT{1}, std::nullopt};

    ::SendMessageW(handle, WM_CLOSE, 0, 0);
    ::SendMessageW(handle, WM_CLOSE, 0, 0);
    if (window.close_request_count != 1 || window.flutter_close_message_count != 0 ||
        !window.close_completion) {
        return false;
    }
    window.close_completion();
    if (!DispatchCloseCompletion(handle) ||
        window.flutter_close_message_count != 1 || !::IsWindow(handle)) {
        return false;
    }

    ::SendMessageW(handle, WM_CLOSE, 0, 0);
    return window.close_request_count == 1 &&
           window.flutter_close_message_count == 2 && !::IsWindow(handle);
}

///  Verifies an unavailable Dart channel still offers WM_CLOSE to Flutter.
///  @return `true` when the window handler delegates without waiting or retrying cleanup.
bool MissingLifecycleChannelDelegatesClose() {
    flutter::DartProject project(L"data");
    TestFlutterWindow window(project);
    HWND handle = CreateTestWindow(window);
    if (handle == nullptr) {
        return false;
    }
    window.close_request_available = false;
    window.flutter_results = {LRESULT{1}, LRESULT{1}};

    ::SendMessageW(handle, WM_CLOSE, 0, 0);
    ::SendMessageW(handle, WM_CLOSE, 0, 0);
    const bool succeeded = window.close_request_count == 1 &&
                           window.flutter_close_message_count == 2 &&
                           ::IsWindow(handle);
    return succeeded;
}

///  Verifies the native timer wins over a late Dart completion at the handler boundary.
///  @return `true` when stale timer IDs are ignored and late replies cannot delegate twice.
bool TimeoutAndLateReplyAreHandledByRunner() {
    flutter::DartProject project(L"data");
    TestFlutterWindow window(project);
    HWND handle = CreateTestWindow(window);
    if (handle == nullptr) {
        return false;
    }
    window.flutter_results = {LRESULT{1}};

    ::SendMessageW(handle, WM_CLOSE, 0, 0);
    const std::optional<std::uint64_t> generation =
        window.PendingCloseGeneration();
    if (!generation.has_value()) {
        return false;
    }
    ::SendMessageW(handle, WM_TIMER,
                   static_cast<WPARAM>(generation.value() + 1), 0);
    if (!window.PendingCloseGeneration().has_value() ||
        window.flutter_close_message_count != 0) {
        return false;
    }

    ::SendMessageW(handle, WM_TIMER, static_cast<WPARAM>(generation.value()), 0);
    if (window.flutter_close_message_count != 1 || !::IsWindow(handle)) {
        return false;
    }
    window.close_completion();
    const bool succeeded = DispatchCloseCompletion(handle) &&
                           window.flutter_close_message_count == 1 &&
                           window.close_request_count == 1;
    return succeeded;
}

///  Verifies session-query and end-session messages preserve their fast, one-shot contract.
///  @return `true` when query is immediate and only committed session end requests cleanup once.
bool SessionEndMessagesKeepTheirNativeContract() {
    flutter::DartProject project(L"data");
    TestFlutterWindow window(project);
    HWND handle = CreateTestWindow(window);
    if (handle == nullptr) {
        return false;
    }
    const LRESULT queryResult =
        ::SendMessageW(handle, WM_QUERYENDSESSION, 0, 0);
    const int cleanupRequestsAfterQuery = window.session_end_request_count;
    ::SendMessageW(handle, WM_ENDSESSION, FALSE, 0);
    const int cleanupRequestsAfterCancellation = window.session_end_request_count;
    ::SendMessageW(handle, WM_ENDSESSION, TRUE, 0);
    ::SendMessageW(handle, WM_ENDSESSION, TRUE, 0);
    const bool succeeded = queryResult == TRUE && cleanupRequestsAfterQuery == 0 &&
                           cleanupRequestsAfterCancellation == 0 &&
                           window.flutter_session_message_count == 0 &&
                           window.session_end_request_count == 1;
    return succeeded;
}

///  Runs native window-lifecycle state tests.
///  @return `EXIT_SUCCESS` when every lifecycle condition is satisfied.
int main() {
    if (!BeginRejectsDuplicateRequests()) {
        return 1;
    }
    if (!CompleteRejectsStaleAndRepeatedTokens()) {
        return 2;
    }
    if (!BeginRejectsTheEmptyGeneration()) {
        return 3;
    }
    if (!TimeoutMakesLateDartReplyStale()) {
        return 4;
    }
    if (!TimerCreationFailureAllowsOneClose()) {
        return 5;
    }
    if (!CloseCompletionUsesFlutterWindowPipeline()) {
        return 6;
    }
    if (!MissingLifecycleChannelDelegatesClose()) {
        return 7;
    }
    if (!TimeoutAndLateReplyAreHandledByRunner()) {
        return 8;
    }
    if (!SessionEndMessagesKeepTheirNativeContract()) {
        return 9;
    }
    if (!SessionEndCleanupRunsOnceAfterCommit()) {
        return 10;
    }
    if (!ResetStartsFreshWindowLifecycle()) {
        return 11;
    }
    return EXIT_SUCCESS;
}

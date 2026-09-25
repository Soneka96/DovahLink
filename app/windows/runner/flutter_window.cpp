#include "flutter_window.h"

#include <atomic>
#include <cassert>
#include <cstdint>
#include <optional>
#include <utility>

#include <flutter/method_result_functions.h>

#include "flutter/generated_plugin_registrant.h"

namespace {

///  Message posted after Dart responds to a normal close request.
constexpr UINT kCloseRequestCompletedMessage = WM_APP + 1;

///  Maximum time the normal close handshake waits for Dart cleanup.
constexpr UINT kNativeCloseRequestTimeoutMilliseconds = 5000;

///  Method channel shared with the Flutter app's Windows lifecycle bridge.
constexpr char kWindowLifecycleChannelName[] = "dovahlink/window_lifecycle";

///  Dart method that performs app-owned cleanup before a user close continues.
constexpr char kRequestCloseMethod[] = "requestClose";

///  Dart method that starts best-effort cleanup for committed Windows session ending.
constexpr char kRequestSessionEndMethod[] = "requestSessionEnd";

///  Generates unique close tokens across windows recreated within this process.
std::atomic<std::uint64_t> g_nextCloseRequestGeneration{1};

///  Returns a nonzero token unique to this process's close-request sequence.
std::uint64_t NextCloseRequestGeneration() noexcept {
    std::uint64_t generation =
        g_nextCloseRequestGeneration.fetch_add(1, std::memory_order_relaxed);
    if (generation == 0) {
        generation =
            g_nextCloseRequestGeneration.fetch_add(1, std::memory_order_relaxed);
    }
    return generation;
}

///  Posts completion for [generation] without accessing window-owned state.
///  @param window The window that began the request.
///  @param generation The close request being completed.
void PostCloseCompletion(HWND window, std::uint64_t generation) noexcept {
    ::PostMessage(window, kCloseRequestCompletedMessage,
                  static_cast<WPARAM>(generation), 0);
}

} //  namespace

FlutterWindow::FlutterWindow(
    const flutter::DartProject& project,
    std::unique_ptr<IWindowLifecycleState> lifecycleState)
    : project_(project), lifecycle_state_(std::move(lifecycleState)) {
    assert(lifecycle_state_ != nullptr);
}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
    if (!Win32Window::OnCreate()) {
        return false;
    }
    lifecycle_state_->ResetForWindow();

    RECT frame = GetClientArea();

    //  The size here must match the window dimensions to avoid unnecessary surface
    //  creation / destruction in the startup path.
    flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
        frame.right - frame.left, frame.bottom - frame.top, project_);
    //  Ensure that basic setup of the controller was successful.
    if (!flutter_controller_->engine() || !flutter_controller_->view()) {
        return false;
    }
    RegisterPlugins(flutter_controller_->engine());
    window_lifecycle_channel_ =
        std::make_unique<flutter::MethodChannel<flutter::EncodableValue>>(
            flutter_controller_->engine()->messenger(),
            kWindowLifecycleChannelName,
            &flutter::StandardMethodCodec::GetInstance());
    SetChildContent(flutter_controller_->view()->GetNativeWindow());

    flutter_controller_->engine()->SetNextFrameCallback([&]() { this->Show(); });

    //  Flutter can complete the first frame before the "show window" callback is
    //  registered. The following call ensures a frame is pending to ensure the
    //  window is shown. It is a no-op if the first frame hasn't completed yet.
    flutter_controller_->ForceRedraw();

    return true;
}

void FlutterWindow::OnDestroy() {
    const std::optional<std::uint64_t> generation =
        lifecycle_state_->PendingCloseGeneration();
    if (generation.has_value()) {
        if (GetHandle() != nullptr) {
            ::KillTimer(GetHandle(), static_cast<UINT_PTR>(generation.value()));
        }
    }
    lifecycle_state_->ResetForWindow();
    window_lifecycle_channel_ = nullptr;
    if (flutter_controller_) {
        flutter_controller_ = nullptr;
    }

    Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
    switch (message) {
    case WM_QUERYENDSESSION:
        return TRUE;

    case WM_ENDSESSION:
        if (window_lifecycle_channel_ &&
            lifecycle_state_->BeginSessionEndCleanup(wparam != FALSE)) {
            window_lifecycle_channel_->InvokeMethod(
                kRequestSessionEndMethod,
                std::make_unique<flutter::EncodableValue>(),
                std::make_unique<flutter::MethodResultFunctions<
                    flutter::EncodableValue>>(
                    [](const flutter::EncodableValue*) {},
                    [](const std::string&, const std::string&,
                       const flutter::EncodableValue*) {},
                    []() {}));
        }
        return Win32Window::MessageHandler(hwnd, message, wparam, lparam);

    case WM_CLOSE: {
        if (!window_lifecycle_channel_) {
            return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
        }
        if (lifecycle_state_->HasPendingClose()) {
            return 0;
        }
        const std::uint64_t generation = NextCloseRequestGeneration();
        if (!lifecycle_state_->BeginClose(generation)) {
            return 0;
        }
        const UINT_PTR timerId = ::SetTimer(
            hwnd, static_cast<UINT_PTR>(generation),
            kNativeCloseRequestTimeoutMilliseconds, nullptr);
        const auto postCloseCompletion = [hwnd, generation]() {
            PostCloseCompletion(hwnd, generation);
        };
        window_lifecycle_channel_->InvokeMethod(
            kRequestCloseMethod, std::make_unique<flutter::EncodableValue>(),
            std::make_unique<flutter::MethodResultFunctions<
                flutter::EncodableValue>>(
                [postCloseCompletion](const flutter::EncodableValue*) {
                    postCloseCompletion();
                },
                [postCloseCompletion](const std::string&, const std::string&,
                                      const flutter::EncodableValue*) {
                    postCloseCompletion();
                },
                [postCloseCompletion]() { postCloseCompletion(); }));
        if (timerId == 0) {
            if (lifecycle_state_->CompleteClose(generation)) {
                return Win32Window::MessageHandler(hwnd, WM_CLOSE, 0, 0);
            }
        }
        return 0;
    }

    case WM_TIMER: {
        const std::optional<std::uint64_t> generation =
            lifecycle_state_->PendingCloseGeneration();
        if (generation.has_value() &&
            generation.value() == static_cast<std::uint64_t>(wparam)) {
            if (lifecycle_state_->CompleteClose(generation.value())) {
                ::KillTimer(hwnd, static_cast<UINT_PTR>(generation.value()));
                return Win32Window::MessageHandler(hwnd, WM_CLOSE, 0, 0);
            }
            return 0;
        }
        break;
    }

    case kCloseRequestCompletedMessage: {
        const std::uint64_t generation = static_cast<std::uint64_t>(wparam);
        if (!lifecycle_state_->CompleteClose(generation)) {
            return 0;
        }
        ::KillTimer(hwnd, static_cast<UINT_PTR>(generation));
        return Win32Window::MessageHandler(hwnd, WM_CLOSE, 0, 0);
    }
    }

    //  Give Flutter, including plugins, an opportunity to handle window messages.
    if (flutter_controller_) {
        std::optional<LRESULT> result =
            flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                          lparam);
        if (result) {
            return *result;
        }
    }

    switch (message) {
    case WM_FONTCHANGE:
        flutter_controller_->engine()->ReloadSystemFonts();
        break;
    }

    return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}

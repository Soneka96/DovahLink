#include "flutter_window.h"

#include <optional>

#include <flutter/method_result_functions.h>

#include "flutter/generated_plugin_registrant.h"

namespace {

///  Message posted after Dart finishes or rejects the shutdown request.
constexpr UINT kCloseRequestCompletedMessage = WM_APP + 1;

///  Method channel shared with the Flutter app's shutdown service.
constexpr char kWindowLifecycleChannelName[] = "dovahlink/window_lifecycle";

///  Dart method that performs app-owned cleanup before returning its channel response.
constexpr char kRequestCloseMethod[] = "requestClose";

} //  namespace

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
    if (!Win32Window::OnCreate()) {
        return false;
    }

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
    case WM_CLOSE: {
        if (!window_lifecycle_channel_) {
            return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
        }
        if (close_request_pending_) {
            return 0;
        }
        close_request_pending_ = true;
        const auto postCloseCompletion = [hwnd]() {
            ::PostMessage(hwnd, kCloseRequestCompletedMessage, 0, 0);
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
        return 0;
    }
    case kCloseRequestCompletedMessage:
        if (!close_request_pending_) {
            return 0;
        }
        close_request_pending_ = false;
        return Win32Window::MessageHandler(hwnd, WM_CLOSE, 0, 0);
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

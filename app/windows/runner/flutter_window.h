#ifndef RUNNER_FLUTTER_WINDOW_H_
#define RUNNER_FLUTTER_WINDOW_H_

#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>
#include <flutter/method_channel.h>
#include <flutter/standard_method_codec.h>

#include <functional>
#include <memory>
#include <optional>

#include "window_lifecycle_state.h"

#include "win32_window.h"

///  A native window that hosts a Flutter view.
class FlutterWindow : public Win32Window {
  public:
    ///  Creates a window that hosts the supplied Flutter project.
    ///  @param project The Flutter project hosted by this window.
    ///  @param lifecycleState Non-null state that deduplicates close and session-end requests.
    FlutterWindow(const flutter::DartProject& project,
                  std::unique_ptr<IWindowLifecycleState> lifecycleState);

    ///  Destroys the window and its Flutter controller.
    virtual ~FlutterWindow();

  protected:
    ///  @copydoc Win32Window::OnCreate
    bool OnCreate() override;

    ///  @copydoc Win32Window::OnDestroy
    void OnDestroy() override;

    ///  @copydoc Win32Window::MessageHandler
    LRESULT MessageHandler(HWND window, UINT const message, WPARAM const wparam,
                           LPARAM const lparam) noexcept override;

    ///  Starts app-owned cleanup and posts [completion] when Dart replies.
    ///  @param completion Posts the matching native close-completion message.
    ///  @return `true` when the lifecycle channel accepted the request.
    virtual bool RequestDartClose(std::function<void()> completion) noexcept;

    ///  Starts best-effort Dart cleanup after Windows commits to ending the session.
    ///  @return `true` when the lifecycle channel accepted the request.
    virtual bool RequestDartSessionEndCleanup() noexcept;

    ///  Gives Flutter's engine and plugins an opportunity to handle a top-level window message.
    ///  @param window The native top-level window receiving the message.
    ///  @param message The Win32 message to offer to Flutter.
    ///  @param wparam The message's first native parameter.
    ///  @param lparam The message's second native parameter.
    ///  @return The handled result, or `std::nullopt` when Flutter declines the message.
    virtual std::optional<LRESULT> HandleFlutterWindowProc(
        HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept;

  private:
    ///  The Flutter project executed by this window.
    flutter::DartProject project_;

    ///  The Flutter instance hosted by this window.
    std::unique_ptr<flutter::FlutterViewController> flutter_controller_;

    ///  Channel used to wait for Dart-owned cleanup before processing a close request.
    std::unique_ptr<flutter::MethodChannel<flutter::EncodableValue>>
        window_lifecycle_channel_;

    ///  Owns the active close and session-ending state for this native window.
    std::unique_ptr<IWindowLifecycleState> lifecycle_state_;
};

#endif //  RUNNER_FLUTTER_WINDOW_H_

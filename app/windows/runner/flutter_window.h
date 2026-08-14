#ifndef RUNNER_FLUTTER_WINDOW_H_
#define RUNNER_FLUTTER_WINDOW_H_

#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>

#include <memory>

#include "win32_window.h"

/// A native window that hosts a Flutter view.
class FlutterWindow : public Win32Window {
 public:
  /// Creates a window that hosts the supplied Flutter project.
  explicit FlutterWindow(const flutter::DartProject& project);

  /// Destroys the window and its Flutter controller.
  virtual ~FlutterWindow();

 protected:
  /// @copydoc Win32Window::OnCreate
  bool OnCreate() override;

  /// @copydoc Win32Window::OnDestroy
  void OnDestroy() override;

  /// @copydoc Win32Window::MessageHandler
  LRESULT MessageHandler(HWND window, UINT const message, WPARAM const wparam,
                         LPARAM const lparam) noexcept override;

 private:
  /// The Flutter project executed by this window.
  flutter::DartProject project_;

  /// The Flutter instance hosted by this window.
  std::unique_ptr<flutter::FlutterViewController> flutter_controller_;
};

#endif  // RUNNER_FLUTTER_WINDOW_H_

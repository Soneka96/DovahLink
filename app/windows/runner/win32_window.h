#ifndef RUNNER_WIN32_WINDOW_H_
#define RUNNER_WIN32_WINDOW_H_

#include <windows.h>

#include <functional>
#include <memory>
#include <string>

/// A high-DPI-aware Win32 window base for custom rendering and input handling.
class Win32Window {
 public:
  /// A logical window origin in screen coordinates.
  struct Point {
    /// The horizontal screen coordinate.
    unsigned int x;

    /// The vertical screen coordinate.
    unsigned int y;

    /// Creates a point from screen coordinates.
    Point(unsigned int x, unsigned int y) : x(x), y(y) {}
  };

  /// A logical window size.
  struct Size {
    /// The window width.
    unsigned int width;

    /// The window height.
    unsigned int height;

    /// Creates a size from width and height values.
    Size(unsigned int width, unsigned int height)
        : width(width), height(height) {}
  };

  /// Creates an uninitialized native window wrapper.
  Win32Window();

  /// Destroys the native window wrapper and releases its resources.
  virtual ~Win32Window();

  /// Creates an invisible window positioned and sized on the default monitor.
  bool Create(const std::wstring& title, const Point& origin, const Size& size);

  /// Shows the current window and reports whether it was shown successfully.
  bool Show();

  /// Releases OS resources associated with the window.
  void Destroy();

  /// Inserts the supplied native content into the window tree.
  void SetChildContent(HWND content);

  /// Returns the backing window handle, or `nullptr` after destruction.
  HWND GetHandle();

  /// Sets whether closing this window quits the application.
  void SetQuitOnClose(bool quit_on_close);

  /// Returns the bounds of the current client area.
  RECT GetClientArea();

 protected:
  /// Routes window messages to the native window and subclasses.
  virtual LRESULT MessageHandler(HWND window,
                                 UINT const message,
                                 WPARAM const wparam,
                                 LPARAM const lparam) noexcept;

  /// Performs subclass setup during creation and reports success.
  virtual bool OnCreate();

  /// Performs subclass cleanup during destruction.
  virtual void OnDestroy();

 private:
  friend class WindowClassRegistrar;

  /// Dispatches OS callbacks to the associated Win32Window instance.
  static LRESULT CALLBACK WndProc(HWND const window,
                                  UINT const message,
                                  WPARAM const wparam,
                                  LPARAM const lparam) noexcept;

  /// Retrieves the Win32Window instance associated with the native window.
  static Win32Window* GetThisFromHandle(HWND const window) noexcept;

  /// Updates the window frame theme to match the system preference.
  static void UpdateTheme(HWND const window);

  /// Whether closing the window quits the application.
  bool quit_on_close_ = false;

  /// The native handle for the top-level window.
  HWND window_handle_ = nullptr;

  /// The native handle for hosted Flutter content.
  HWND child_content_ = nullptr;
};

#endif  // RUNNER_WIN32_WINDOW_H_

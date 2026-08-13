import Cocoa
import FlutterMacOS

/// Coordinates the macOS application lifecycle for the Flutter runner.
@main
class AppDelegate: FlutterAppDelegate {
  /// Keeps the application running only while a runner window remains open.
  override func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
    return true
  }

  /// Enables secure state restoration for the macOS runner.
  override func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
    return true
  }
}

import Cocoa
import FlutterMacOS

/// The macOS window that hosts the Flutter view.
class MainFlutterWindow: NSWindow {
  /// Creates and attaches the Flutter view when the window is loaded.
  override func awakeFromNib() {
    let flutterViewController = FlutterViewController()
    let windowFrame = self.frame
    self.contentViewController = flutterViewController
    self.setFrame(windowFrame, display: true)

    RegisterGeneratedPlugins(registry: flutterViewController)

    super.awakeFromNib()
  }
}

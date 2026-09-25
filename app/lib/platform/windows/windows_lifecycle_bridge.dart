import 'package:flutter/services.dart';

import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';

/// Defines the Windows native-to-Dart lifecycle bridge.
abstract interface class IWindowsLifecycleBridge {
  /// Registers the native lifecycle method handler on its channel.
  void register();
}

/// Routes native Windows close and session-ending requests to shared app shutdown.
class WindowsLifecycleBridge implements IWindowsLifecycleBridge {
  /// Method channel used by the Windows runner.
  final MethodChannel _channel;

  /// Platform-neutral owner of app cleanup.
  final IAppShutdownService _shutdownService;

  /// Creates the bridge for the Windows runner's lifecycle channel.
  /// @param channel The native method channel used to request cleanup.
  /// @param shutdownService The shared shutdown coordinator.
  WindowsLifecycleBridge({
    required MethodChannel channel,
    required IAppShutdownService shutdownService,
  }) : _channel = channel,
       _shutdownService = shutdownService;

  /// Implements [IWindowsLifecycleBridge.register].
  @override
  void register() {
    _channel.setMethodCallHandler((MethodCall call) async {
      if (call.method != 'requestClose' && call.method != 'requestSessionEnd') {
        throw MissingPluginException('Unsupported Windows lifecycle method.');
      }
      await _shutdownService.shutdown();
    });
  }
}

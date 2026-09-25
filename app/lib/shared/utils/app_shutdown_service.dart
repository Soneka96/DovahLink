import 'package:flutter/services.dart';

import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Defines app cleanup and the Windows window-close request handler.
abstract interface class IAppShutdownService {
  /// Registers the native Windows close request method handler.
  void registerWindowCloseHandler();

  /// Stops app-owned recovery and disconnects the SDK client once.
  /// @return A future completing after cleanup or the bounded shutdown wait.
  Future<void> shutdown();
}

/// Coordinates app-owned cleanup and the existing pairing disconnect use case.
class AppShutdownService implements IAppShutdownService {
  /// Pairing middleware whose retry timer and connection subscription belong to the app.
  final IPairingMiddleware _pairingMiddleware;

  /// Existing use case that delegates session teardown to the SDK client.
  final DisconnectUseCase _disconnectUseCase;

  /// Native close-request channel whose reply lets the Windows runner continue closing.
  final MethodChannel _windowChannel;

  /// The shared cleanup operation returned to every shutdown caller.
  Future<void>? _shutdownFuture;

  /// Maximum time allowed for SDK disconnect before the close request continues.
  static const Duration _shutdownTimeout = Duration(seconds: 3);

  /// Creates the shutdown owner from app resources and the Windows lifecycle channel.
  /// @param pairingMiddleware The middleware owning app-level retry and status observation.
  /// @param disconnectUseCase The application use case delegating to SDK disconnect.
  /// @param windowChannel The method channel used by the Windows runner close handshake.
  AppShutdownService({
    required IPairingMiddleware pairingMiddleware,
    required DisconnectUseCase disconnectUseCase,
    required MethodChannel windowChannel,
  }) : _pairingMiddleware = pairingMiddleware,
       _disconnectUseCase = disconnectUseCase,
       _windowChannel = windowChannel;

  /// Implements [IAppShutdownService.registerWindowCloseHandler].
  @override
  void registerWindowCloseHandler() {
    _windowChannel.setMethodCallHandler((MethodCall call) async {
      if (call.method != 'requestClose') {
        throw MissingPluginException('Unsupported Windows lifecycle method.');
      }
      await shutdown();
    });
  }

  /// Implements [IAppShutdownService.shutdown].
  @override
  Future<void> shutdown() => _shutdownFuture ??= _performShutdown().timeout(
    _shutdownTimeout,
    onTimeout: () {},
  );

  /// Stops app-owned recovery first, then awaits SDK disconnect within [_shutdownTimeout].
  /// Cleanup failures are contained so the native close request can still finish.
  /// @return A future that completes after both cleanup attempts settle.
  Future<void> _performShutdown() async {
    Future<void>? pairingCleanup;
    try {
      pairingCleanup = _pairingMiddleware.shutdown();
    } on Object {
      // SDK disconnect must still run when app-owned cleanup cannot start.
    }
    try {
      await _disconnectUseCase(NoParams()).timeout(_shutdownTimeout);
    } on Object {
      // A failed or stalled transport must not keep the native window open.
    }
    try {
      await pairingCleanup;
    } on Object {
      // Stream cancellation is best-effort after SDK recovery and transport have stopped.
    }
  }
}

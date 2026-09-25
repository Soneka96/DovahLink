import 'package:dovahlink_client/features/pairing/presentation/state/pairing.middleware.dart';
import 'package:dovahlink_client/shared/utils/existing_dovahlink_client.dart';

/// Defines the platform-neutral cleanup path for app-owned Dart resources.
abstract interface class IAppShutdownService {
  /// Stops pairing work and disconnects an SDK client that already exists.
  ///
  /// Concurrent calls share one bounded cleanup operation. Cleanup failures do not escape.
  /// @return A future completing when cleanup finishes or the shutdown budget expires.
  Future<void> shutdown();
}

/// Coordinates pairing cleanup and disconnect of an existing SDK client.
class AppShutdownService implements IAppShutdownService {
  /// Pairing middleware whose retry timer and connection subscription belong to the app.
  final IPairingMiddleware _pairingMiddleware;

  /// Accesses only an SDK client already created by pairing composition.
  final IExistingDovahLinkClient _existingClient;

  /// The shared cleanup operation returned to every shutdown caller.
  Future<void>? _shutdownFuture;

  /// Maximum time allowed for app-owned cleanup before shutdown proceeds.
  static const Duration _shutdownTimeout = Duration(seconds: 3);

  /// Creates the shared shutdown owner from app-owned resource contracts.
  /// @param pairingMiddleware The middleware owning app-level retry and status observation.
  /// @param existingClient The holder that disconnects only an SDK client already constructed.
  AppShutdownService({
    required IPairingMiddleware pairingMiddleware,
    required IExistingDovahLinkClient existingClient,
  }) : _pairingMiddleware = pairingMiddleware,
       _existingClient = existingClient;

  /// Implements [IAppShutdownService.shutdown].
  @override
  Future<void> shutdown() => _shutdownFuture ??= _performShutdown().timeout(
    _shutdownTimeout,
    onTimeout: () {},
  );

  /// Stops pairing work, attempts client disconnect, and contains both cleanup failures.
  Future<void> _performShutdown() async {
    final Future<void> pairingCleanup = _stopPairing();
    try {
      await _existingClient.disconnectIfCreated();
    } on Object {
      // A failed transport must not keep application close pending.
    }
    await pairingCleanup;
  }

  /// Stops pairing middleware and contains synchronous or asynchronous cancellation failures.
  Future<void> _stopPairing() async {
    try {
      await _pairingMiddleware.shutdown();
    } on Object {
      // Client disconnect must still run when app-owned cleanup cannot complete.
    }
  }
}

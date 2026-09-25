import 'dart:async';

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
  Future<void> shutdown() => _shutdownFuture ??= _performShutdown();

  /// Stops pairing work before starting the existing-client disconnect. Both operations begin
  /// before the deadline wait, so disconnect invalidates authentication and reconnect work
  /// immediately. Their late completions perform no follow-up application work.
  Future<void> _performShutdown() async {
    final Completer<void> deadline = Completer<void>();
    final Timer timer = Timer(_shutdownTimeout, deadline.complete);
    try {
      final Future<void> pairingCleanup = _stopPairing();
      final Future<void> clientDisconnect = _disconnectExistingClient();
      await Future.any<void>([
        Future.wait<void>([pairingCleanup, clientDisconnect]),
        deadline.future,
      ]);
    } on Object {
      // Cleanup failures must not keep the native window open.
    } finally {
      timer.cancel();
    }
  }

  /// Stops pairing middleware and contains synchronous or asynchronous cancellation failures.
  Future<void> _stopPairing() async {
    try {
      await _pairingMiddleware.shutdown();
    } on Object {
      // Continue to client disconnect if the deadline still permits another cleanup step.
    }
  }

  /// Disconnects the already-created SDK client as the final bounded cleanup step.
  Future<void> _disconnectExistingClient() async {
    try {
      await _existingClient.disconnectIfCreated();
    } on Object {
      // A failed transport must not keep the native window open.
    }
  }
}

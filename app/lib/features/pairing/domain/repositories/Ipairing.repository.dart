import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Domain boundary for negotiating local device pairing with the bridge.
abstract interface class IPairingRepository {
  /// Connects and authenticates, resolving this installation's trust
  /// standing. Recovers an interrupted pairing confirmation automatically
  /// when the session authenticates as unpaired.
  Future<Either<Failure, PairingHandshakeEntity>> authenticate();

  /// Starts, or queries the status of, a pairing challenge. A fresh or
  /// already-active code is shown in Skyrim; this resolves once the client
  /// may show its code-entry form.
  Future<Either<Failure, Unit>> requestPairingCode();

  /// Submits the six-digit code the user read from Skyrim and completes the
  /// trust handshake.
  Future<Either<Failure, Unit>> confirmPairingCode({
    required String code,
    String? displayName,
  });

  /// Closes the current connection without changing game state.
  Future<Either<Failure, Unit>> disconnect();

  /// Requests redisplay of the active pairing code in Skyrim, or reports
  /// cooldown or idle status.
  Future<Either<Failure, Unit>> requestPairingRenotify();

  /// Cancels the owned active pairing challenge or pending credential, or
  /// reports idle status.
  Future<Either<Failure, Unit>> cancelPairing();
}

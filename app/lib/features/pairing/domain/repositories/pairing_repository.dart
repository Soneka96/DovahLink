import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Domain boundary for negotiating local device pairing with the host.
abstract interface class IPairingRepository {
  /// Connects to the Host at [hostUri] and authenticates, resolving this
  /// installation's trust standing with that Host. Recovers an interrupted
  /// pairing confirmation automatically when the session authenticates as
  /// unpaired.
  Future<Either<Failure, PairingHandshake>> authenticate({
    required Uri hostUri,
  });

  /// Starts, or queries the status of, a pairing challenge. A fresh or
  /// already-active code is shown in Skyrim; this resolves once the client
  /// may show its code-entry form. Returns the active code's remaining
  /// validity in seconds, or null when the host did not report one.
  Future<Either<Failure, int?>> requestPairingCode();

  /// Submits the six-digit code the user read from Skyrim and completes the
  /// trust handshake.
  Future<Either<Failure, Unit>> confirmPairingCode({
    required String code,
    String? displayName,
  });

  /// Closes the current connection without changing game state.
  Future<Either<Failure, Unit>> disconnect();

  /// Requests redisplay of the active pairing code in Skyrim, or reports
  /// idle status. Returns Host-reported retry seconds after successful redisplay or
  /// during cooldown.
  Future<Either<Failure, int?>> requestPairingRenotify();

  /// Cancels the owned active pairing challenge or pending credential, or
  /// reports idle status.
  Future<Either<Failure, Unit>> cancelPairing();

  /// Emits every change in the host connection's status while a session is active -- ordinary
  /// transport loss and recovery, and administrative invalidation, unified rather than split
  /// into a narrower administrative-only slice -- including one that arrives with nothing
  /// pending. Never completes and carries no request of its own.
  Stream<PairingConnectionStatus> get connectionStatus;
}

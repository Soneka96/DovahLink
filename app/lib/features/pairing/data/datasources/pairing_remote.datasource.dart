import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    hide PairingRenotifyResult;

import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/data/models/pairing_handshake.model.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart';
import 'package:dovahlink_client/features/pairing/pairing_failure.mapper.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Wraps grouped SDK APIs for the pairing feature's remote operations.
abstract interface class IPairingRemoteDataSource {
  /// Authenticates and recovers an interrupted confirmation through the SDK. A left target is an
  /// untrusted endpoint candidate; a right target is a Known Host ID.
  Future<Either<Failure, PairingHandshakeModel>> authenticate({
    required Either<Uri, String> target,
  });

  /// Starts, or queries the status of, a pairing challenge.
  /// Returns the active code's remaining validity in seconds, or null when the host did not
  /// report one.
  Future<Either<Failure, int?>> requestPairingCode();

  /// Submits the six-digit code and completes the trust handshake.
  Future<Either<Failure, Unit>> confirmPairingCode({
    required String code,
    String? displayName,
  });

  /// Closes the current connection.
  Future<Either<Failure, Unit>> disconnect();

  /// Requests redisplay of the active pairing code in Skyrim.
  /// Returns Host-reported retry seconds, including the cooldown that starts after
  /// successful redisplay.
  Future<Either<Failure, PairingRenotifyResult>> requestPairingRenotify();

  /// Cancels the owned active pairing challenge or pending credential.
  Future<Either<Failure, Unit>> cancelPairing();

  /// Emits every change in the host connection's status while a session is active -- ordinary
  /// transport loss and recovery, and administrative invalidation, unified rather than split
  /// into a narrower administrative-only slice -- including one that arrives with nothing
  /// pending -- unlike every method above, this never completes and carries no request of its
  /// own.
  Stream<PairingConnectionStatus> get connectionStatus;
}

/// The user-safe [Failure] reported for any exception this data source's typed catches don't
/// recognize; shared by every method so an unexpected failure reads identically everywhere.
const PairingFailure _unexpectedPairingFailure = PairingFailure(
  'Pairing could not be completed. Please try again.',
);

/// Authenticates the candidate endpoint or Known Host ID through an injected
/// [DovahLinkClient], converting its typed exceptions into user-safe [Failure]s. An exception
/// outside that documented set is also converted rather than left to escape this boundary, as
/// [_unexpectedPairingFailure].
class PairingRemoteDataSource implements IPairingRemoteDataSource {
  /// The wrapped SDK client.
  final DovahLinkClient _client;

  /// Creates a data source backed by [_client].
  PairingRemoteDataSource(this._client);

  /// See [IPairingRemoteDataSource.authenticate]. The SDK owns connection, authentication, and
  /// pending-pairing recovery; this layer maps its typed result to presentation-safe data.
  @override
  Future<Either<Failure, PairingHandshakeModel>> authenticate({
    required Either<Uri, String> target,
  }) async {
    try {
      final DovahLinkPairingHandshake handshake = await target.fold(
        _client.pairing.authenticateCandidate,
        (String hostId) =>
            _client.pairing.authenticateKnownHost(DovahLinkHostId(hostId)),
      );
      return Right(PairingHandshakeModel.fromPairingHandshake(handshake));
    } on DovahLinkConnectionException catch (error) {
      // Administrative invalidation (revoked/blocked/trustReset/factoryReset) can fail this same
      // pending call with a generic DovahLinkConnectionException; distinguishing it here through
      // the SDK's already-public connectionState prevents the caller from treating it as ordinary
      // transport loss eligible for silent automatic retry.
      if (_client.connections.state ==
          DovahLinkConnectionState.administrativelyInvalidated) {
        return const Left(SessionInvalidatedFailure.administrative);
      }
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException {
      return const Left(_unexpectedPairingFailure);
    } on DovahLinkPairingException catch (error) {
      return Left(
        PairingFailureMapper.fromSdkException(
              error,
              fallbackMessage: _unexpectedPairingFailure.message,
            ) ??
            _unexpectedPairingFailure,
      );
    } on DovahLinkStorageException catch (error) {
      return Left(DatabaseFailure(error.message));
    } on Object {
      // The typed catches above are the SDK's documented failure surface for this call; anything
      // else is unexpected and must not escape past this boundary (ai/context/flutter/
      // error-handling.md's "Never let raw infrastructure exceptions escape into a use case or
      // presentation").
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [IPairingRemoteDataSource.requestPairingCode].
  @override
  Future<Either<Failure, int?>> requestPairingCode() async {
    try {
      final PairingChallengeStatus status = await _client.pairing.requestCode();
      if (status.availability == PairingAvailability.unavailable) {
        return const Left(
          PairingFailure(
            'Pairing is not available right now. Try again in a moment.',
          ),
        );
      }
      if (status.availability == PairingAvailability.otherDevicePairing) {
        // Reveals nothing about the owning device or its code, matching
        // ai/context/protocol/security.md's other_device_pairing contract.
        return const Left(
          PairingFailure(
            'Another device is already pairing. Try again in a moment.',
          ),
        );
      }
      return Right(status.expiresInSeconds);
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [IPairingRemoteDataSource.confirmPairingCode].
  @override
  Future<Either<Failure, Unit>> confirmPairingCode({
    required String code,
    String? displayName,
  }) async {
    try {
      await _client.pairing.confirmCode(code: code, displayName: displayName);
      return const Right(unit);
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      return Left(
        PairingFailureMapper.fromSdkException(
              error,
              fallbackMessage: _unexpectedPairingFailure.message,
              keepCodeEntry: true,
            ) ??
            _unexpectedPairingFailure,
      );
    } on DovahLinkStorageException catch (error) {
      return Left(DatabaseFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [IPairingRemoteDataSource.disconnect].
  @override
  Future<Either<Failure, Unit>> disconnect() async {
    try {
      await _client.connections.disconnect();
      return const Right(unit);
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [IPairingRemoteDataSource.requestPairingRenotify].
  @override
  Future<Either<Failure, PairingRenotifyResult>>
  requestPairingRenotify() async {
    try {
      final renotifyResult = await _client.pairing.renotify();
      return Right(
        PairingRenotifyResult(
          outcome: switch (renotifyResult.status) {
            PairingRenotifyStatus.renotified =>
              PairingRenotifyOutcome.renotified,
            PairingRenotifyStatus.cooldown => PairingRenotifyOutcome.cooldown,
            PairingRenotifyStatus.alreadyIdle =>
              PairingRenotifyOutcome.alreadyIdle,
          },
          retryAfterSeconds: renotifyResult.retryAfterSeconds,
        ),
      );
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      return Left(
        PairingFailureMapper.fromSdkException(
              error,
              fallbackMessage: _unexpectedPairingFailure.message,
            ) ??
            _unexpectedPairingFailure,
      );
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [IPairingRemoteDataSource.cancelPairing].
  @override
  Future<Either<Failure, Unit>> cancelPairing() async {
    try {
      final cancelOutcome = await _client.pairing.cancel();
      return switch (cancelOutcome.status) {
        PairingCancelStatus.cancelled => const Right(unit),
        PairingCancelStatus.alreadyIdle => const Right(unit),
      };
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      return Left(
        PairingFailureMapper.fromSdkException(
              error,
              fallbackMessage: _unexpectedPairingFailure.message,
            ) ??
            _unexpectedPairingFailure,
      );
    } on DovahLinkStorageException catch (error) {
      return Left(DatabaseFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [IPairingRemoteDataSource.connectionStatus]. `connecting` carries no distinct status for
  /// this feature -- it never occurs for a trusted session's own bounded recovery (see
  /// `SessionState.beginConnectAttempt`'s "reconnecting" mid-recovery guard), and this data
  /// source is only ever observed post-trust -- so it maps to `null` and is filtered out before
  /// the stream is cast down to its non-nullable element type. [Stream] has no `whereType`
  /// equivalent to [Iterable.whereType], so `where` plus `cast` is the standard substitute.
  /// `reauthenticating` -- the transport back up but not yet re-trusted during recovery -- maps to
  /// `lost` alongside `reconnecting`/`disconnected`, not `restored`: this feature must not report
  /// the connection restored before the session is actually re-authenticated.
  @override
  Stream<PairingConnectionStatus> get connectionStatus => _client
      .connections
      .stateChanges
      .map(
        (DovahLinkConnectionState state) => switch (state) {
          DovahLinkConnectionState.reconnecting ||
          DovahLinkConnectionState.reauthenticating ||
          DovahLinkConnectionState.disconnected => PairingConnectionStatus.lost,
          DovahLinkConnectionState.connected =>
            PairingConnectionStatus.restored,
          DovahLinkConnectionState.administrativelyInvalidated =>
            PairingConnectionStatus.invalidated,
          DovahLinkConnectionState.connecting => null,
        },
      )
      .where((PairingConnectionStatus? status) => status != null)
      .cast<PairingConnectionStatus>();
}

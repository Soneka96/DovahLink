import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Wraps a [DovahLinkClient] for the pairing feature's remote operations.
abstract interface class PairingRemoteDataSource {
  /// Connects and authenticates, recovering an interrupted pairing
  /// confirmation when the session authenticates as unpaired.
  Future<Either<Failure, PairingHandshakeEntity>> authenticate();

  /// Starts, or queries the status of, a pairing challenge.
  /// Returns the active code's remaining validity in seconds, or null when the bridge did not
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
  /// Returns cooldown seconds if in cooldown, null if succeeded.
  Future<Either<Failure, int?>> requestPairingRenotify();

  /// Cancels the owned active pairing challenge or pending credential.
  Future<Either<Failure, Unit>> cancelPairing();

  /// Emits every change in the bridge connection's status while a session is active -- ordinary
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

/// Connects to the shared default Bridge endpoint ([defaultBridgeUri]) through an injected
/// [DovahLinkClient], converting its typed exceptions into user-safe [Failure]s. An exception
/// outside that documented set is also converted rather than left to escape this boundary, as
/// [_unexpectedPairingFailure].
class PairingRemoteDataSourceImpl implements PairingRemoteDataSource {
  /// Creates a data source backed by [_client].
  PairingRemoteDataSourceImpl(this._client);

  /// The wrapped SDK client.
  final DovahLinkClient _client;

  /// See [PairingRemoteDataSource.authenticate]. Delegates to [DovahLinkClient.authenticate],
  /// which recovers from a rejected `trusted_device_credential` hello by discarding the stale
  /// credential and retrying as `unpaired` -- this layer only picks the user-safe wording for
  /// [HelloResult.recoveredFromRejectedCredential] when that happened.
  @override
  Future<Either<Failure, PairingHandshakeEntity>> authenticate() async {
    try {
      final HelloResult hello = await _client.authenticate(defaultBridgeUri);
      bool trusted = hello.trustState == DovahLinkTrustState.trusted;
      if (!trusted) {
        final DovahLinkTrustState recovered = await _client
            .recoverPendingPairing();
        trusted = recovered == DovahLinkTrustState.trusted;
      }
      return Right(
        PairingHandshakeEntity(
          bridgeVersion: hello.bridgeVersion,
          trusted: trusted,
          credentialRejectedMessage: _credentialRejectedMessage(
            hello.recoveredFromRejectedCredential,
          ),
        ),
      );
    } on DovahLinkConnectionException catch (error) {
      // Administrative invalidation (revoked/blocked/trustReset/factoryReset) can fail this same
      // pending call with a generic DovahLinkConnectionException; distinguishing it here through
      // the SDK's already-public connectionState prevents the caller from treating it as ordinary
      // transport loss eligible for silent automatic retry, per PLAN.md's Stage 3.
      if (_client.connectionState ==
          DovahLinkConnectionState.administrativelyInvalidated) {
        return const Left(SessionInvalidatedFailure.administrative);
      }
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      return Left(PairingFailure(_pairingOutcomeMessage(error.outcome)));
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

  /// Converts a recovered credential-rejection reason into its user-safe explanation, or `null`
  /// when [reason] is `null` (nothing was recovered from).
  String? _credentialRejectedMessage(
    CredentialRejectionReason? reason,
  ) => switch (reason) {
    CredentialRejectionReason.revoked =>
      "This device's trust was revoked. Requesting a new pairing code.",
    CredentialRejectionReason.unrecognized =>
      "This device isn't recognized by this bridge. Requesting a new pairing code.",
    CredentialRejectionReason.blocked =>
      'This device is blocked by the bridge and cannot be paired again until an '
          'administrator unblocks it.',
    null => null,
  };

  /// See [PairingRemoteDataSource.requestPairingCode].
  @override
  Future<Either<Failure, int?>> requestPairingCode() async {
    try {
      final PairingChallengeStatus status = await _client.requestPairing();
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

  /// See [PairingRemoteDataSource.confirmPairingCode].
  @override
  Future<Either<Failure, Unit>> confirmPairingCode({
    required String code,
    String? displayName,
  }) async {
    try {
      final String credential = await _client.confirmPairingCode(
        code: code,
        displayName: displayName,
      );
      await _client.acknowledgeTrustedCredential(credential);
      return const Right(unit);
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      final String message = _pairingOutcomeMessage(error.outcome);
      // Only a wrong code or a too-soon retry are retriable against the same still-active
      // challenge: everything else (expired, hard_limit_reached, pending_not_found) ends the
      // flow, matching PLAN.md stage I's "keeps a short-of-hard-limit wrong code on
      // awaitingCode... instead of bouncing to failed" versus "maps hard_limit_reached to the
      // existing PairingFailedAction path."
      if (error.outcome == PairingOutcome.invalid ||
          error.outcome == PairingOutcome.pacingLimited) {
        return Left(PairingRetriableFailure(message));
      }
      return Left(PairingFailure(message));
    } on DovahLinkStorageException catch (error) {
      return Left(DatabaseFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [PairingRemoteDataSource.disconnect].
  @override
  Future<Either<Failure, Unit>> disconnect() async {
    try {
      await _client.disconnect();
      return const Right(unit);
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// Converts a `pairing_confirm`/`pairing_ack` outcome into a
  /// user-safe message.
  String _pairingOutcomeMessage(PairingOutcome outcome) => switch (outcome) {
    PairingOutcome.expired =>
      'That pairing code has expired. Request a new one.',
    PairingOutcome.invalid =>
      "That code isn't correct. Check Skyrim and try again.",
    PairingOutcome.pacingLimited => 'Slow down a little, then try again.',
    PairingOutcome.hardLimitReached =>
      'Too many wrong attempts. Request a new pairing code.',
    PairingOutcome.pendingNotFound =>
      'This pairing attempt is no longer recognized. Request a new code.',
    _ => 'Pairing could not be completed. Please try again.',
  };

  /// See [PairingRemoteDataSource.requestPairingRenotify].
  @override
  Future<Either<Failure, int?>> requestPairingRenotify() async {
    try {
      final renotifyResult = await _client.requestPairingRenotify();
      return switch (renotifyResult.status) {
        PairingRenotifyStatus.renotified => const Right(null),
        PairingRenotifyStatus.cooldown => Right(
          renotifyResult.retryAfterSeconds,
        ),
        PairingRenotifyStatus.alreadyIdle => const Left(
          PairingFailure('No pairing is currently active.'),
        ),
      };
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      return Left(PairingFailure(_pairingOutcomeMessage(error.outcome)));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [PairingRemoteDataSource.cancelPairing].
  @override
  Future<Either<Failure, Unit>> cancelPairing() async {
    try {
      final cancelOutcome = await _client.cancelPairing();
      return switch (cancelOutcome.status) {
        PairingCancelStatus.cancelled => const Right(unit),
        PairingCancelStatus.alreadyIdle => const Right(unit),
      };
    } on DovahLinkConnectionException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkProtocolException catch (error) {
      return Left(NetworkFailure(error.message));
    } on DovahLinkPairingException catch (error) {
      return Left(PairingFailure(_pairingOutcomeMessage(error.outcome)));
    } on DovahLinkStorageException catch (error) {
      return Left(DatabaseFailure(error.message));
    } on Object {
      return const Left(_unexpectedPairingFailure);
    }
  }

  /// See [PairingRemoteDataSource.connectionStatus]. `connecting` carries no distinct status for
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
      .connectionStateChanges
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

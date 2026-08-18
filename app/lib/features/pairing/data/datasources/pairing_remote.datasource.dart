import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Wraps a [DovahLinkClient] for the pairing feature's remote operations.
abstract interface class PairingRemoteDataSource {
  /// Connects and authenticates, recovering an interrupted pairing
  /// confirmation when the session authenticates as unpaired.
  Future<Either<Failure, PairingHandshakeEntity>> authenticate();

  /// Starts, or queries the status of, a pairing challenge.
  Future<Either<Failure, Unit>> requestPairingCode();

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
  String? _credentialRejectedMessage(CredentialRejectionReason? reason) =>
      switch (reason) {
        CredentialRejectionReason.revoked =>
          "This device's trust was revoked. Requesting a new pairing code.",
        CredentialRejectionReason.unrecognized =>
          "This device isn't recognized by this bridge. Requesting a new pairing code.",
        null => null,
      };

  /// See [PairingRemoteDataSource.requestPairingCode].
  @override
  Future<Either<Failure, Unit>> requestPairingCode() async {
    try {
      final PairingAvailability availability = await _client.requestPairing();
      if (availability == PairingAvailability.unavailable) {
        return const Left(
          PairingFailure(
            'Pairing is not available right now. Try again in a moment.',
          ),
        );
      }
      return const Right(unit);
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
      return Left(PairingFailure(_pairingOutcomeMessage(error.outcome)));
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

  /// Converts a raw `pairing_confirm`/`pairing_ack` outcome into a
  /// user-safe message.
  String _pairingOutcomeMessage(String outcome) => switch (outcome) {
    'expired' => 'That pairing code has expired. Request a new one.',
    'invalid' => "That code isn't correct. Check Skyrim and try again.",
    'rate_limited' => 'Too many attempts. Wait a moment before trying again.',
    _ => 'Pairing could not be completed. Please try again.',
  };

  /// See [PairingRemoteDataSource.requestPairingRenotify].
  @override
  Future<Either<Failure, int?>> requestPairingRenotify() async {
    try {
      final renotifyResult = await _client.requestPairingRenotify();
      return switch (renotifyResult.status) {
        PairingRenotifyStatus.renotified => const Right(null),
        PairingRenotifyStatus.cooldown => Right(renotifyResult.retryAfterSeconds),
        PairingRenotifyStatus.alreadyIdle =>
          const Left(PairingFailure('No pairing is currently active.')),
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
}

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
}

/// Connects to the shared default Bridge endpoint ([defaultBridgeUri]) through an injected
/// [DovahLinkClient], converting its typed exceptions into user-safe [Failure]s.
class PairingRemoteDataSourceImpl implements PairingRemoteDataSource {
  /// Creates a data source backed by [_client].
  PairingRemoteDataSourceImpl(this._client);

  /// The wrapped SDK client.
  final DovahLinkClient _client;

  /// See [PairingRemoteDataSource.authenticate].
  @override
  Future<Either<Failure, PairingHandshakeEntity>> authenticate() async {
    try {
      await _client.connect(defaultBridgeUri);
      final HelloResult hello = await _client.hello();
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
    }
  }

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
}

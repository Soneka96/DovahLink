import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Implements [IPairingRepository] over [IPairingRemoteDataSource].
class PairingRepositoryImpl implements IPairingRepository {
  /// Creates a repository backed by [_remoteDataSource].
  PairingRepositoryImpl(this._remoteDataSource);

  /// The remote data source this repository delegates to.
  final IPairingRemoteDataSource _remoteDataSource;

  /// See [IPairingRepository.authenticate].
  @override
  Future<Either<Failure, PairingHandshakeEntity>> authenticate() =>
      _remoteDataSource.authenticate();

  /// See [IPairingRepository.requestPairingCode].
  @override
  Future<Either<Failure, int?>> requestPairingCode() =>
      _remoteDataSource.requestPairingCode();

  /// See [IPairingRepository.confirmPairingCode].
  @override
  Future<Either<Failure, Unit>> confirmPairingCode({
    required String code,
    String? displayName,
  }) => _remoteDataSource.confirmPairingCode(
    code: code,
    displayName: displayName,
  );

  /// See [IPairingRepository.disconnect].
  @override
  Future<Either<Failure, Unit>> disconnect() => _remoteDataSource.disconnect();

  /// See [IPairingRepository.requestPairingRenotify].
  @override
  Future<Either<Failure, int?>> requestPairingRenotify() =>
      _remoteDataSource.requestPairingRenotify();

  /// See [IPairingRepository.cancelPairing].
  @override
  Future<Either<Failure, Unit>> cancelPairing() =>
      _remoteDataSource.cancelPairing();

  /// See [IPairingRepository.connectionStatus].
  @override
  Stream<PairingConnectionStatus> get connectionStatus =>
      _remoteDataSource.connectionStatus;
}

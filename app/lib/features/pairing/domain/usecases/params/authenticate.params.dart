import 'package:equatable/equatable.dart';
import 'package:fpdart/fpdart.dart';
import 'package:meta/meta.dart';

/// Parameters for connecting and authenticating with a Host.
@immutable
class AuthenticateParams extends Equatable {
  /// An untrusted candidate endpoint in `Left`, or a stable Known Host ID in `Right`.
  final Either<Uri, String> target;

  /// Creates candidate-authentication parameters for [hostUri].
  AuthenticateParams({required Uri hostUri}) : target = Left(hostUri);

  /// Creates Known Host-authentication parameters for [hostId].
  AuthenticateParams.knownHost({required String hostId})
    : target = Right(hostId);

  /// See [Equatable.props].
  @override
  List<Object?> get props => [target];
}

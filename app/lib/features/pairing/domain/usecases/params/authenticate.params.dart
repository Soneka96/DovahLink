import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

/// Parameters for connecting and authenticating with a Host.
@immutable
class AuthenticateParams extends Equatable {
  /// The WebSocket endpoint of the Host to connect to.
  final Uri hostUri;

  /// Creates authentication parameters for the Host at [hostUri].
  const AuthenticateParams({required this.hostUri});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [hostUri];
}

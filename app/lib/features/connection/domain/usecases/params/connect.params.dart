import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

/// Parameters for connecting to the bridge.
@immutable
class ConnectParams extends Equatable {
  /// One-time credential used to authenticate the bridge session.
  final String token;

  /// This installation's persisted logical client identity, supplied in the
  /// hello payload.
  final String clientId;

  /// Creates connection parameters.
  const ConnectParams({required this.token, required this.clientId});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [token, clientId];
}

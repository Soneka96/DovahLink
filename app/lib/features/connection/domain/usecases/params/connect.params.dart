import 'package:equatable/equatable.dart';
import 'package:meta/meta.dart';

/// Parameters for connecting to the bridge.
@immutable
class ConnectParams extends Equatable {
  /// One-time credential used to authenticate the bridge session.
  final String token;

  /// Creates connection parameters.
  const ConnectParams({required this.token});

  /// See [Equatable.props].
  @override
  List<Object?> get props => [token];
}
